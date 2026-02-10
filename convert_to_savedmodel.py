import tensorflow as tf
import os

# غيري المسار ده لو فولدر الموديل في مكان تاني
keras_model_path = r"C:\Users\adminstrator\Downloads\best_skin_disease_model.keras"

export_path = r"C:\Users\adminstrator\Downloads\SkinAI.API\AI\skin_model_savedmodel\"

model = tf.keras.models.load_model(keras_model_path, compile=False)
tf.saved_model.save(model, export_path)

print("✅ Model converted successfully to SavedModel at:", os.path.abspath(export_path))
