# -*- coding: utf-8 -*-
import os
import io
import time
import threading
import urllib.request

import numpy as np
import tensorflow as tf
from PIL import Image, ImageOps
from fastapi import FastAPI, UploadFile, File, HTTPException
from fastapi.responses import JSONResponse

print("🔥 USING UPDATED SERVER.PY (RENDER SAFE) 🔥")

# ------------------ FastAPI App ------------------
app = FastAPI(title="SkinAI FastAPI (Mobile)")

# Optional: reduce TF logs
os.environ.setdefault("TF_CPP_MIN_LOG_LEVEL", "2")
os.environ.setdefault("TF_ENABLE_ONEDNN_OPTS", "0")

API_VERSION = "mobile-1.2.0"
BUILD_TIME_UTC = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())

# ------------------ Custom Layers ------------------
def swish(x):
    return tf.nn.swish(x)

# ------------------ Paths ------------------
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
MODEL_PATH  = os.path.join(BASE_DIR, "best_skin_disease_model.keras")
LABELS_PATH = os.path.join(BASE_DIR, "labels.txt")
MODEL_URL = os.environ.get("MODEL_URL")

print("✅ FILE:", __file__)
print("✅ LABELS_PATH =", LABELS_PATH)
print("✅ MODEL_PATH =", MODEL_PATH)

# ------------------ Labels ------------------
IMG_SIZE = (380, 380)

DEFAULT_LABELS = [
    "Eczema",
    "Psoriasis",
    "Skin Cancer",
    "Tinea",
    "Normal",
    "Vitiligo"
]

def load_labels():
    try:
        if os.path.exists(LABELS_PATH):
            with open(LABELS_PATH, "r", encoding="utf-8") as f:
                labels = [line.strip() for line in f if line.strip()]
            if len(labels) == 6:
                return labels
    except Exception as e:
        print("⚠️ Failed reading labels.txt:", e)
    return DEFAULT_LABELS

LABELS = load_labels()

# ------------------ Model holder (IMPORTANT) ------------------
model = None
model_err = None
model_ready_at_utc = None

# ------------------ Thresholds ------------------
CONF_THRESHOLD = 0.70
GAP_THRESHOLD = 0.12
NORMAL_ACCEPT_THRESHOLD = 0.65

# ------------------ Mobile Image Quality Policy ------------------
MIN_W = 256
MIN_H = 256
WARN_W = 600
WARN_H = 600
MAX_UPLOAD_BYTES = 8 * 1024 * 1024  # 8MB

def safe_label(i: int) -> str:
    return LABELS[i] if 0 <= i < len(LABELS) else f"L{i}"

def ensure_softmax(probs: np.ndarray) -> np.ndarray:
    probs = np.array(probs).reshape(-1)
    s = float(np.sum(probs))
    if s < 0.9 or s > 1.1:
        probs = tf.nn.softmax(probs).numpy()
    return probs.astype(np.float32)

def topk(probs: np.ndarray, k: int = 3):
    idx = probs.argsort()[-k:][::-1].tolist()
    return [{"index": int(i), "label": safe_label(int(i)), "score": float(probs[int(i)])} for i in idx]

def ranked_all(probs: np.ndarray):
    order = probs.argsort()[::-1].tolist()
    return [{"index": int(i), "label": safe_label(int(i)), "score": float(probs[int(i)])} for i in order]

def compute_gap(probs: np.ndarray) -> float:
    p = np.sort(probs)
    return float(p[-1] - p[-2]) if p.size >= 2 else 0.0

def resize_then_center_crop(img: Image.Image, size=(380, 380)) -> Image.Image:
    target_w, target_h = size
    w, h = img.size
    scale = max(target_w / w, target_h / h)
    nw, nh = int(w * scale), int(h * scale)
    img_resized = img.resize((nw, nh), Image.BILINEAR)

    left = (nw - target_w) // 2
    top = (nh - target_h) // 2
    right = left + target_w
    bottom = top + target_h
    return img_resized.crop((left, top, right, bottom))

def quality_tier(w: int, h: int) -> str:
    if w < MIN_W or h < MIN_H:
        return "bad"
    if w < WARN_W or h < WARN_H:
        return "low"
    return "good"

def msg(code: str, en: str, **extra):
    payload = {"code": code, "en": en}
    if extra:
        payload.update(extra)
    return payload

def message_for(status: str, tier: str):
    if status == "bad_image":
        return msg(
            "BAD_IMAGE",
            "Image is too small/unclear. Please upload a clearer photo (avoid WhatsApp compressed images).",
            min_w=MIN_W, min_h=MIN_H
        )
    if tier == "low" and status in ("ok", "uncertain", "low_quality"):
        return msg(
            "LOW_IMAGE_QUALITY",
            "Low image quality. For better accuracy: use good lighting and zoom so the lesion fills most of the frame.",
            warn_w=WARN_W, warn_h=WARN_H
        )
    if status == "uncertain":
        return msg(
            "UNCERTAIN_RESULT",
            "Result is uncertain. Please take a clearer photo or consult a dermatologist."
        )
    return msg(
        "OK_RESULT",
        "This result is for guidance only and is not a final medical diagnosis."
    )

def preprocess(image_bytes: bytes):
    img = Image.open(io.BytesIO(image_bytes))
    img = ImageOps.exif_transpose(img).convert("RGB")

    w, h = img.size
    tier = quality_tier(w, h)
    if tier == "bad":
        raise ValueError(f"bad_image: image too small ({w}x{h})")

    img = resize_then_center_crop(img, IMG_SIZE)

    x = np.array(img).astype("float32")
    x = tf.keras.applications.efficientnet.preprocess_input(x)
    x = np.expand_dims(x, 0)
    return x, (w, h), tier

def decide(probs: np.ndarray, tier: str):
    probs = np.array(probs).reshape(-1)
    best = int(np.argmax(probs))
    best_label = safe_label(best)
    conf = float(probs[best])
    gap = compute_gap(probs)
    t3 = topk(probs, 3)

    # (keep your original logic)
    if best_label == "Unknown_Normal" and conf >= NORMAL_ACCEPT_THRESHOLD:
        status = "ok" if tier == "good" else "low_quality"
        return status, best, best_label, conf, gap, t3, None

    if conf < CONF_THRESHOLD or gap < GAP_THRESHOLD:
        status = "uncertain" if tier == "good" else "low_quality"
        uncertain_reason = {
            "type": "low_confidence_or_close_scores",
            "conf_threshold": CONF_THRESHOLD,
            "gap_threshold": GAP_THRESHOLD,
            "top2": [
                {"label": t3[0]["label"], "score": float(t3[0]["score"])} if len(t3) > 0 else None,
                {"label": t3[1]["label"], "score": float(t3[1]["score"])} if len(t3) > 1 else None
            ]
        }
        return status, best, best_label, conf, gap, t3, uncertain_reason

    status = "ok" if tier == "good" else "low_quality"
    return status, best, best_label, conf, gap, t3, None

# ------------------ Model loader (runs in background) ------------------
def _download_model_if_needed():
    if os.path.exists(MODEL_PATH):
        return
    if not MODEL_URL:
        raise RuntimeError(
            f"Model file not found at: {MODEL_PATH}\n"
            f"Either place the model there OR set MODEL_URL environment variable."
        )
    os.makedirs(os.path.dirname(MODEL_PATH), exist_ok=True)
    print("⬇️ Downloading model from MODEL_URL ...")
    urllib.request.urlretrieve(MODEL_URL, MODEL_PATH)
    print("✅ Model downloaded:", MODEL_PATH)

def _load_model_background():
    global model, model_err, model_ready_at_utc

    try:
        print("✅ Background: preparing model...")
        _download_model_if_needed()

        print("✅ Loading model...")

        model = tf.keras.models.load_model(
            MODEL_PATH,
            compile=False,
            safe_mode=False
        )

        print("🚀 Model loaded successfully")
        model_ready_at_utc = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())

    except Exception as e:
        print("❌ Model loading failed:", e)
        model_err = str(e)



        # warmup
        dummy = np.zeros((1, IMG_SIZE[0], IMG_SIZE[1], 3), dtype=np.float32)
        _ = model.predict(dummy, verbose=0)
        print("✅ Model warmup done")

        model_ready_at_utc = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
        model_err = None
    except Exception as e:
        model = None
        model_err = str(e)
        print("❌ Model load failed:", model_err)

@app.on_event("startup")
def startup():
    # IMPORTANT: start loading in background so uvicorn opens the port quickly
    t = threading.Thread(target=_load_model_background, daemon=True)
    t.start()
    print("✅ Startup: model loading thread started")

def require_model():
    if model is None:
        # If failed, show why
        if model_err:
            raise HTTPException(status_code=500, detail=f"Model failed to load: {model_err}")
        raise HTTPException(status_code=503, detail="Model is still loading. Try again in a few seconds.")

# ------------------ Endpoints ------------------
@app.get("/ping")
def ping():
    return {
        "ok": True,
        "api_version": API_VERSION,
        "build_time_utc": BUILD_TIME_UTC,
        "labels": LABELS,
        "model_loaded": model is not None,
        "model_ready_at_utc": model_ready_at_utc,
        "model_error": model_err,
        "max_upload_bytes": MAX_UPLOAD_BYTES
    }

@app.post("/predict")
async def predict(file: UploadFile = File(...)):
    require_model()

    try:
        image_bytes = await file.read()
        if not image_bytes:
            raise HTTPException(status_code=400, detail="Empty file")

        if len(image_bytes) > MAX_UPLOAD_BYTES:
            return JSONResponse(
                status_code=413,
                content={"ok": False, "error": "File too large", "max_upload_bytes": MAX_UPLOAD_BYTES}
            )

        x, (w, h), tier = preprocess(image_bytes)

        pred = model.predict(x, verbose=0)
        raw = pred[0] if pred.ndim >= 2 else pred.reshape(-1)

        probs = ensure_softmax(raw)
        status, best, diagnosis, conf, gap, t3, uncertain_reason = decide(probs, tier)
        message = message_for("bad_image" if status == "bad_image" else status, tier if status != "low_quality" else "low")

        return {
            "ok": True,
            "status": status,
            "diagnosis": diagnosis,
            "confidence": conf,
            "class_index": best,
            "gap_top1_top2": gap,
            "top3": t3,
            "uncertain_reason": uncertain_reason,
            "message": message,
            "quality": {"w": w, "h": h, "tier": tier},
            "meta": {"api_version": API_VERSION, "img_size": list(IMG_SIZE)}
        }

    except ValueError as ve:
        msg_txt = str(ve)
        if msg_txt.startswith("bad_image:"):
            message = message_for("bad_image", "bad")
            return JSONResponse(
                status_code=200,
                content={
                    "ok": True,
                    "status": "bad_image",
                    "diagnosis": "Unknown_Normal",
                    "confidence": 0.0,
                    "message": message,
                    "details": msg_txt.replace("bad_image:", "").strip(),
                    "quality": {"tier": "bad", "min": [MIN_W, MIN_H]},
                    "meta": {"api_version": API_VERSION}
                }
            )
        return JSONResponse(status_code=400, content={"ok": False, "error": msg_txt})

    except HTTPException:
        raise
    except Exception as e:
        return JSONResponse(status_code=500, content={"ok": False, "error": str(e)})

@app.post("/predict_debug")
async def predict_debug(file: UploadFile = File(...)):
    require_model()

    try:
        image_bytes = await file.read()
        if not image_bytes:
            raise HTTPException(status_code=400, detail="Empty file")

        if len(image_bytes) > MAX_UPLOAD_BYTES:
            return JSONResponse(
                status_code=413,
                content={"ok": False, "error": "File too large", "max_upload_bytes": MAX_UPLOAD_BYTES}
            )

        x, (w, h), tier = preprocess(image_bytes)
        pred = model.predict(x, verbose=0)
        raw = pred[0] if pred.ndim >= 2 else pred.reshape(-1)

        probs = ensure_softmax(raw)
        status, best, diagnosis, conf, gap, t3, uncertain_reason = decide(probs, tier)
        message = message_for("bad_image" if status == "bad_image" else status, tier if status != "low_quality" else "low")

        return {
            "ok": True,
            "status": status,
            "best": {"index": best, "label": diagnosis, "score": conf},
            "gap_top1_top2": gap,
            "ranked_all": ranked_all(probs),
            "uncertain_reason": uncertain_reason,
            "message": message,
            "quality": {"w": w, "h": h, "tier": tier},
            "meta": {"api_version": API_VERSION, "img_size": list(IMG_SIZE)}
        }

    except HTTPException:
        raise
    except Exception as e:
        return JSONResponse(status_code=500, content={"ok": False, "error": str(e)})
