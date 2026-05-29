import os
import cv2
import pandas as pd

# --- CONFIGURATION ---
CSV_PATH = "PaintingData/6_painting_20260527_154517.csv"  # Path to your uploaded CSV
IMAGE_FOLDER = "Assets/RX-Ray Images"  # Change to your actual images folder
OUTPUT_FOLDER = "./annotated_images"  # Where the marked images will be saved
RADIUS = 25  # Size of the coordinate point
THICKNESS = -1  # -1 fills the circle completely


# --- PROPORTIONAL CANVAS RESOLUTION ---
# Change these to match your Unity texture/canvas size if the coordinates aren't right.
# (If your coordinates are normalized fractions between 0.0 and 1.0, set these to 1.0)
VIRTUAL_CANVAS_WIDTH = 1
VIRTUAL_CANVAS_HEIGHT = 1


def hex_to_bgr(hex_str):
    """Converts a hex color string (e.g., 'DC3132') to BGR tuple for OpenCV."""
    hex_str = str(hex_str).lstrip("#")
    if len(hex_str) < 6:
        hex_str = hex_str.ljust(6, "0")
    r = int(hex_str[0:2], 16)
    g = int(hex_str[2:4], 16)
    b = int(hex_str[4:6], 16)
    return (b, g, r)


def main():
    # 1. Create output directory if it doesn't exist
    os.makedirs(OUTPUT_FOLDER, exist_ok=True)

    # 2. Load the coordinates dataframe
    print(f"Reading CSV: {CSV_PATH}")
    df = pd.read_csv(CSV_PATH)

    # 3. Group data by ImageName
    grouped = df.groupby("ImageName")

    for image_name, group in grouped:
        if str(image_name).endswith(".meta"):
            continue

        # Construct full path to the source image
        img_path = os.path.join(IMAGE_FOLDER, str(image_name))

        if not os.path.exists(img_path):
            # Fallback extensions if omitted in CSV strings
            img_path_png = img_path + ".png"
            img_path_jpg = img_path + ".jpg"
            if os.path.exists(img_path_png):
                img_path = img_path_png
            elif os.path.exists(img_path_jpg):
                img_path = img_path_jpg
            else:
                print(
                    f"⚠️ Warning: Image '{image_name}' not found in folder. Skipping."
                )
                continue

        # Load the image
        img = cv2.imread(img_path)
        if img is None:
            print(f"❌ Failed to load image matrix: {img_path}")
            continue

        # Get actual image size
        img_h, img_w, _ = img.shape
        print(
            f"Processing {os.path.basename(img_path)} ({img_w}x{img_h}) | Found {len(group)} data points..."
        )

        # Set to track unique labels already printed on this specific image
        printed_labels = set()

        # 4. Iterate through coordinates
        for idx, row in group.iterrows():
            raw_x = float(row["NormalizedX"])
            raw_y = float(row["NormalizedY"])

            # Map coordinates proportionally to native image dimensions
            scaled_x = int((raw_x / VIRTUAL_CANVAS_WIDTH) * img_w)

            # NOTE: If your Y coordinates are inverted vertically (upside down), swap with:
            scaled_y = int((1.0 - (raw_y / VIRTUAL_CANVAS_HEIGHT)) * img_h)
            #scaled_y = int((raw_y / VIRTUAL_CANVAS_HEIGHT) * img_h)

            # Force boundaries inside image coordinates array bounds
            scaled_x = max(0, min(scaled_x, img_w - 1))
            scaled_y = max(0, min(scaled_y, img_h - 1))

            color_hex = str(row["ColorHex"])
            label = str(row["Label"])
            color_bgr = hex_to_bgr(color_hex)

            # ALWAYS draw the colored dot/circle
            cv2.circle(img, (scaled_x, scaled_y), RADIUS, color_bgr, THICKNESS)

            # ONLY draw the text label if it hasn't been used yet on this image
            if label not in printed_labels:
                text_position = (scaled_x + RADIUS + 8, scaled_y + 5)
                cv2.putText(
                    img,
                    label,
                    text_position,
                    cv2.FONT_HERSHEY_SIMPLEX,
                    1.5,  # Font size
                    color_bgr,
                    2,  # Font thickness
                    cv2.LINE_AA,
                )
                # Remember this label so it isn't generated again
                printed_labels.add(label)

        # 5. Save the final annotated file
        output_path = os.path.join(OUTPUT_FOLDER, os.path.basename(img_path))
        cv2.imwrite(output_path, img)

    print(f"\n All done! Check clean annotated images in: {OUTPUT_FOLDER}")


if __name__ == "__main__":
    main()