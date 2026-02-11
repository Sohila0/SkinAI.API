# -*- coding: utf-8 -*-
# -*- coding: utf-8 -*-
import os
import io
import urllib.request
import time
import gdown
import numpy as np
import tensorflow as tf
from PIL import Image, ImageOps
from fastapi import FastAPI, UploadFile, File, HTTPException
from fastapi.responses import JSONResponse
print("🔥 USING UPDATED SERVER.PY 🔥")


app = FastAPI()

def swish(x):
    return tf.nn.swish(x)


# Optional: reduce TF logs
os.environ.setdefault("TF_CPP_MIN_LOG_LEVEL", "2")
os.environ.setdefault("TF_ENABLE_ONEDNN_OPTS", "0")

API_VERSION = "mobile-1.2.0"
BUILD_TIME_UTC = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
def swish(x):
    return tf.nn.swish(x)

MODEL_URL = os.environ.get("MODEL_URL")
MODEL_PATH = "best_skin_disease_model.keras"
if not MODEL_URL:
    raise RuntimeError("MODEL_URL environment variable is not set")
if not os.path.exists(MODEL_PATH):
    gdown.download(
        url=MODEL_URL,
        output=MODEL_PATH,
        quiet=False
    )

print("✅ Loading model...")
model = tf.keras.models.load_model(
    MODEL_PATH,
    custom_objects={"swish": swish}
)
print("🚀 Model loaded successfully")
print("🔥 DOWNLOAD BLOCK EXECUTED 🔥")


# ---------- Paths ----------
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
LABELS_PATH = os.path.normpath(os.path.join(BASE_DIR, "..", "AI", "labels.txt"))

IMG_SIZE = (380, 380)

DEFAULT_LABELS = [
    "Eczema",
    "Psoriasis",
    "Skin Cancer",
    "Tinea",
    "Unknown_Normal",
    "Vitiligo"
]

# ---------- Thresholds ----------
CONF_THRESHOLD = 0.70
GAP_THRESHOLD = 0.12
NORMAL_ACCEPT_THRESHOLD = 0.65

# ---------- Mobile Image Quality Policy ----------
MIN_W = 256
MIN_H = 256
WARN_W = 600
WARN_H = 600

# ---------- Upload limits (important for mobile) ----------
# Max accepted bytes (e.g., 8MB)
MAX_UPLOAD_BYTES = 8 * 1024 * 1024

def swish(x):
    return tf.nn.swish(x)

def load_labels():
    if os.path.exists(LABELS_PATH):
        with open(LABELS_PATH, "r", encoding="utf-8") as f:
            labels = [line.strip() for line in f if line.strip()]
        if len(labels) == 6:
            return labels
    return DEFAULT_LABELS

LABELS = load_labels()

# Load model once
model = tf.keras.models.load_model(MODEL_PATH, custom_objects={"swish": swish})

# FastAPI
app = FastAPI(title="SkinAI FastAPI (Mobile)")

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

# -------- Messages (EN only since you said app English) --------
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
    """
    Returns: x, (w,h), tier
    """
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
    """
    Always outputs TOP-1 diagnosis + confidence
    status: ok | uncertain | low_quality
    """
    probs = np.array(probs).reshape(-1)
    best = int(np.argmax(probs))
    best_label = safe_label(best)
    conf = float(probs[best])
    gap = compute_gap(probs)
    t3 = topk(probs, 3)

    # Accept confident normal
    if best_label == "Unknown_Normal" and conf >= NORMAL_ACCEPT_THRESHOLD:
        status = "ok" if tier == "good" else "low_quality"
        return status, best, best_label, conf, gap, t3, None

    # Uncertain if low confidence or close scores
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

# ✅ Warmup on startup to prevent first-call timeout
@app.on_event("startup")
def warmup():
    try:
        dummy = np.zeros((1, IMG_SIZE[0], IMG_SIZE[1], 3), dtype=np.float32)
        _ = model.predict(dummy, verbose=0)
        print("✅ Model warmup done")
    except Exception as e:
        print("⚠️ Warmup failed:", e)

@app.get("/ping")
def ping():
    return {
        "ok": True,
        "api_version": API_VERSION,
        "build_time_utc": BUILD_TIME_UTC,
        "labels": LABELS,
        "input_shape": str(model.input_shape),
        "output_shape": str(model.output_shape),
        "preprocess": "EXIF transpose + resize_then_center_crop + efficientnet.preprocess_input",
        "thresholds": {
            "conf_threshold": CONF_THRESHOLD,
            "gap_threshold": GAP_THRESHOLD,
            "normal_accept_threshold": NORMAL_ACCEPT_THRESHOLD
        },
        "image_quality_policy": {
            "min": [MIN_W, MIN_H],
            "warn": [WARN_W, WARN_H],
            "tiers": ["good", "low", "bad"]
        },
        "max_upload_bytes": MAX_UPLOAD_BYTES
    }

@app.post("/predict")
async def predict(file: UploadFile = File(...)):
    try:
        image_bytes = await file.read()

        if not image_bytes:
            raise HTTPException(status_code=400, detail="Empty file")

        if len(image_bytes) > MAX_UPLOAD_BYTES:
            return JSONResponse(
                status_code=413,
                content={
                    "ok": False,
                    "error": "File too large",
                    "max_upload_bytes": MAX_UPLOAD_BYTES
                }
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

    except Exception as e:
        return JSONResponse(status_code=500, content={"ok": False, "error": str(e)})

@app.post("/predict_debug")
async def predict_debug(file: UploadFile = File(...)):
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

    except Exception as e:
        return JSONResponse(status_code=500, content={"ok": False, "error": str(e)})
