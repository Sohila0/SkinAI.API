# -*- coding: utf-8 -*-
import tensorflow as tf
import keras
import builtins
import types
import sys

print("START")
MODEL_PATH = r"C:\Users\adminstrator\Downloads\SkinAI.API\AI\best_skin_disease_model.keras"

# ---- swish ----
@keras.saving.register_keras_serializable(package="Custom", name="swish")
def swish(x):
    return tf.nn.swish(x)

keras.utils.get_custom_objects()["swish"] = swish
builtins.swish = swish

# ---- FixedDropout (efficientnet.model.FixedDropout) ----
class FixedDropout(keras.layers.Dropout):
    def __init__(self, rate, noise_shape=None, seed=None, **kwargs):
        super().__init__(rate=rate, noise_shape=noise_shape, seed=seed, **kwargs)

# أنشئ module وهمي بنفس الاسم اللي الموديل محفوظ بيه
efficientnet_model_mod = types.ModuleType("efficientnet.model")
efficientnet_model_mod.FixedDropout = FixedDropout
sys.modules["efficientnet.model"] = efficientnet_model_mod

try:
    model = keras.models.load_model(
        MODEL_PATH,
        compile=False,
        custom_objects={
            "swish": swish,
            "FixedDropout": FixedDropout,
        },
    )
    print("OK Model loaded successfully!")
    print("INPUT:", model.input_shape)
    print("OUTPUT:", model.output_shape)
except Exception as e:
    print("ERR Error loading model:")
    print(e)

print("END")
