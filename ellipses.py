import cv2
import pandas as pd
import os

def hex_to_bgr(hex_str):
    """Converts an RGB HEX string to a BGR tuple for OpenCV."""
    hex_str = hex_str.lstrip('#')
    r = int(hex_str[0:2], 16)
    g = int(hex_str[2:4], 16)
    b = int(hex_str[4:6], 16)
    return (b, g, r)

def draw_ellipses_from_csv(csv_path, images_directory, output_directory):
    """
    Reads the ellipses CSV, matches 'ImageName', overlays labeled ellipses,
    and saves the finalized markup image with larger text to a designated folder.
    """
    if not os.path.exists(csv_path):
        print(f"Error: CSV file not found at {csv_path}")
        return

    if not os.path.exists(output_directory):
        os.makedirs(output_directory)
        print(f"Created output directory at: {output_directory}")

    unity_color_labels = {
        "8B0000": "Airway wall thickening",
        "FF0000": "Atelectasis",
        "DC3132": "Consolidation",
        "006400": "Emphysema & High lung volume/emphysema",
        "00FF00": "Enlarged cardiac silhouette",
        "B2FF59": "Enlarged hilum",
        "0000FF": "Fibrosis & Interstitial lung disease",
        "4169E1": "Fracture & Acute fracture",
        "00FFFF": "Groundglass opacity",
        "FFA500": "Hiatal hernia",
        "C71585": "Mass",
        "82BE08": "Nodule",
        "8B4513": "Lung nodule or mass",
        "FFAEA9": "Pleural abnormality",
        "FFE4C4": "Pleural effusion",
        "00BFFF": "Pleural thickening",
        "4B0082": "Pneumothorax",
        "808000": "Pulmonary edema",
        "008B8B": "Quality issue",
        "8A2BE2": "Support devices",
        "FF00FF": "Wide mediastinum & Abnormal mediastinal contour"
    }
    
    color_map = {label: hex_to_bgr(hex_code) for hex_code, label in unity_color_labels.items()}
    default_color = (255, 255, 255)

    df = pd.read_csv(csv_path)
    
    for image_name, group in df.groupby('ImageName'):
        print(f"\nProcessing annotations for image target: '{image_name}'")
        
        target_image_path = None
        for ext in ['.png', '.jpg', '.jpeg', '.bmp']:
            test_path = os.path.join(images_directory, f"{image_name}{ext}")
            if os.path.exists(test_path):
                target_image_path = test_path
                break
                
        if not target_image_path:
            print(f"--> Warning: Could not find image file '{image_name}' in {images_directory}. Skipping.")
            continue

        img = cv2.imread(target_image_path)
        h, w, _ = img.shape

        for _, row in group.iterrows():
            label = str(row['Label']).strip()
            
            x_min = int(row['X_Min'] * w)
            x_max = int(row['X_Max'] * w)
            y_min = int((1.0 - row['Y_Min']) * h)
            y_max = int((1.0 - row['Y_Max']) * h)

            center_x = int(row['Center_X'] * w)
            center_y = int((1.0 - row['Center_Y']) * h)

            box_width = abs(x_max - x_min)
            box_height = abs(y_max - y_min)
            axes = (box_width // 2, box_height // 2)

            color = color_map.get(label, default_color)

            # Draw the bounding ellipse
            cv2.ellipse(img, (center_x, center_y), axes, 0, 0, 360, color, 3)

            # --- BIGGER TEXT TWEAKS HERE ---
            # Adjusted offset to -15 so the larger text sits cleanly above the ellipse line
            highest_y = min(y_min, y_max)
            text_y = highest_y - 15 if highest_y - 15 > 30 else highest_y + 35
            
            # Arguments: (image, text, position, font, font_scale, color, thickness, line_type)
            cv2.putText(img, label, (x_min, text_y), 
                        cv2.FONT_HERSHEY_SIMPLEX, 1.2, color, 3, cv2.LINE_AA)

        output_path = os.path.join(output_directory, f"{image_name}_with_ellipses.png")
        cv2.imwrite(output_path, img)
        print(f"--> Saved to target folder: {output_path}")

if __name__ == "__main__":
    CSV_FILE_PATH = "PaintingData/0_painting_20260525_194721_ellipses.csv"
    IMAGES_FOLDER = "Assets/RX-Ray Images/" 
    OUTPUT_FOLDER = "PaintingData/OutputImages/"

    draw_ellipses_from_csv(CSV_FILE_PATH, IMAGES_FOLDER, OUTPUT_FOLDER)