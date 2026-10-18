"""Muffin VTuber face tracker: Google MediaPipe Face Landmarker, fed by the app.

The app owns the camera (some virtual cameras allow only one reader) and pipes every frame in on stdin as raw
RGB bytes (width x height x 3, top row first). Each frame goes through MediaPipe's Face Landmarker in VIDEO
mode, and the result goes back to the app over UDP on localhost:

    'MPF1'                       magic
    double  read_time            time.time() just after the frame was read: the app matches it to its own
                                 copy of that frame, so the eye and mouth cut-outs come from the same picture
    int32   width, height, faces
    per face:
        478 x (float x, float y, float z)   landmarks, x and y as fractions of the image (MediaPipe's own)
        52 x float                          blendshape scores, in BLENDSHAPES order below
        16 x float                          facial transformation matrix, row by row

A packet is sent for every frame, with 0 faces when nobody is in view. Frames never leave this PC.
Settings follow the Face Object Stream handoff: VIDEO mode, 0.55 confidences, blendshapes and the
transformation matrix on.
"""

import argparse
import socket
import struct
import sys
import time

import numpy as np
import mediapipe as mp
from mediapipe.tasks.python import BaseOptions, vision

# MediaPipe's 52 face blendshapes, in its documented order (sent in this order whatever the result's order).
BLENDSHAPES = [
    "_neutral", "browDownLeft", "browDownRight", "browInnerUp", "browOuterUpLeft", "browOuterUpRight",
    "cheekPuff", "cheekSquintLeft", "cheekSquintRight", "eyeBlinkLeft", "eyeBlinkRight", "eyeLookDownLeft",
    "eyeLookDownRight", "eyeLookInLeft", "eyeLookInRight", "eyeLookOutLeft", "eyeLookOutRight", "eyeLookUpLeft",
    "eyeLookUpRight", "eyeSquintLeft", "eyeSquintRight", "eyeWideLeft", "eyeWideRight", "jawForward", "jawLeft",
    "jawOpen", "jawRight", "mouthClose", "mouthDimpleLeft", "mouthDimpleRight", "mouthFrownLeft",
    "mouthFrownRight", "mouthFunnel", "mouthLeft", "mouthLowerDownLeft", "mouthLowerDownRight",
    "mouthPressLeft", "mouthPressRight", "mouthPucker", "mouthRight", "mouthRollLower", "mouthRollUpper",
    "mouthShrugLower", "mouthShrugUpper", "mouthSmileLeft", "mouthSmileRight", "mouthStretchLeft",
    "mouthStretchRight", "mouthUpperUpLeft", "mouthUpperUpRight", "noseSneerLeft", "noseSneerRight",
]
BLENDSHAPE_INDEX = {name: i for i, name in enumerate(BLENDSHAPES)}
LANDMARKS = 478


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--width", type=int, required=True)
    parser.add_argument("--height", type=int, required=True)
    parser.add_argument("--port", type=int, default=11574)
    parser.add_argument("--model", required=True, help="face_landmarker.task")
    parser.add_argument("--faces", type=int, default=1)
    args = parser.parse_args()

    options = vision.FaceLandmarkerOptions(
        base_options=BaseOptions(model_asset_path=args.model),
        running_mode=vision.RunningMode.VIDEO,
        num_faces=args.faces,
        min_face_detection_confidence=0.55,
        min_face_presence_confidence=0.55,
        min_tracking_confidence=0.55,
        output_face_blendshapes=True,
        output_facial_transformation_matrixes=True,
    )
    landmarker = vision.FaceLandmarker.create_from_options(options)
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    target = ("127.0.0.1", args.port)

    width, height = args.width, args.height
    size = width * height * 3
    buffer = bytearray(size)
    view = memoryview(buffer)
    stdin = sys.stdin.buffer
    started = time.perf_counter()
    last_stamp = -1

    while True:
        got = 0
        while got < size:
            n = stdin.readinto(view[got:])
            if not n:
                return  # the app closed the pipe: stop
            got += n
        read_time = time.time()

        # VIDEO mode wants strictly increasing timestamps in milliseconds.
        stamp = int((time.perf_counter() - started) * 1000.0)
        if stamp <= last_stamp:
            stamp = last_stamp + 1
        last_stamp = stamp

        frame = np.frombuffer(buffer, dtype=np.uint8).reshape(height, width, 3)
        image = mp.Image(image_format=mp.ImageFormat.SRGB, data=np.ascontiguousarray(frame))
        result = landmarker.detect_for_video(image, stamp)

        faces = result.face_landmarks or []
        packet = bytearray(b"MPF1")
        packet += struct.pack("<diii", read_time, width, height, len(faces))
        for i, landmarks in enumerate(faces):
            points = np.zeros((LANDMARKS, 3), dtype="<f4")
            for k, p in enumerate(landmarks[:LANDMARKS]):
                points[k] = (p.x, p.y, p.z)
            packet += points.tobytes()

            scores = np.zeros(len(BLENDSHAPES), dtype="<f4")
            if result.face_blendshapes and i < len(result.face_blendshapes):
                for category in result.face_blendshapes[i]:
                    k = BLENDSHAPE_INDEX.get(category.category_name)
                    if k is not None:
                        scores[k] = category.score
            packet += scores.tobytes()

            matrix = np.eye(4, dtype="<f4")
            if result.facial_transformation_matrixes and i < len(result.facial_transformation_matrixes):
                matrix = np.asarray(result.facial_transformation_matrixes[i], dtype="<f4").reshape(4, 4)
            packet += matrix.tobytes()  # row by row
        try:
            sock.sendto(packet, target)
        except OSError:
            pass  # the app isn't listening yet (or any more)


if __name__ == "__main__":
    main()
