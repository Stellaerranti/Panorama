import xml.etree.ElementTree as ET
from PIL import Image, ImageFile
import os

# Allow Pillow to load truncated images
ImageFile.LOAD_TRUNCATED_IMAGES = True

def parse_xml(xml_file):
    tree = ET.parse(xml_file)
    root = tree.getroot()
    images = []
    
    for obj in root.findall(".//Object"):
        name = obj.get("name")
        filename = obj.get("filename")
        points = obj.findall("Point")
        if len(points) == 2:
            x1, y1 = float(points[0].get("x")), float(points[0].get("y"))
            x2, y2 = float(points[1].get("x")), float(points[1].get("y"))
            images.append({
                "name": name,
                "filename": f"{name}.jpg",  # Assuming images are named as CaptureX.jpg
                "coords": (x1, y1, x2, y2)
            })
    return images

def calculate_canvas_size(images):
    min_x = min(min(img["coords"][0], img["coords"][2]) for img in images)
    max_x = max(max(img["coords"][0], img["coords"][2]) for img in images)
    min_y = min(min(img["coords"][1], img["coords"][3]) for img in images)
    max_y = max(max(img["coords"][1], img["coords"][3]) for img in images)
    return min_x, max_x, min_y, max_y
    
    
def ask_coordinate_system(root):
    import tkinter as tk

    selected_orientation = {"value": None}

    def select_orientation(value):
        selected_orientation["value"] = value
        selector.destroy()

    # Create a top-level window instead of a new root
    selector = tk.Toplevel(root)
    selector.title("Select Coordinate System")
    selector.geometry("400x250")

    tk.Label(selector, text="Choose coordinate system:", font=("Arial", 14)).pack(pady=10)

    orientations = [
        ("Top-Left (x →, y ↓)", "top-left"),
        ("Top-Right (x ←, y ↓)", "top-right"),
        ("Bottom-Left (x →, y ↑)", "bottom-left"),
        ("Bottom-Right (x ←, y ↑)", "bottom-right"),
    ]

    for text, value in orientations:
        btn = tk.Button(selector, text=text, width=30, font=("Arial", 12),
                        command=lambda v=value: select_orientation(v))
        btn.pack(pady=5)

    root.wait_window(selector)  # Wait until the selection window closes
    return selected_orientation["value"] or "top-left"



def stitch_images(xml_file, output_file, image_folder, pixels_per_unit=2500, orientation="top-left"):
    images = parse_xml(xml_file)
    min_x, max_x, min_y, max_y = calculate_canvas_size(images)
    
    canvas_width = int((max_x - min_x) * pixels_per_unit + 3000)
    canvas_height = int((max_y - min_y) * pixels_per_unit + 3000)
    
    canvas = Image.new("RGB", (canvas_width, canvas_height), (255, 255, 255))
    
    for img_data in images:
        img_path = os.path.join(image_folder, img_data["filename"])
        
        if not os.path.exists(img_path):
            print(f"Warning: Image {img_path} not found, skipping.")
            continue
        
        try:
            img = Image.open(img_path)
            x1, y1 = img_data["coords"][0], img_data["coords"][1]
            pixel_x = int((x1 - min_x) * pixels_per_unit)
            pixel_y = int((y1 - min_y) * pixels_per_unit)
            
            # Adjust position based on orientation
            if orientation == "top-left":
                pass  # Default: x →, y ↓
            elif orientation == "top-right":
                pixel_x = canvas_width - pixel_x - img.width  # x ←, y ↓
            elif orientation == "bottom-left":
                pixel_y = canvas_height - pixel_y - img.height  # x →, y ↑
            elif orientation == "bottom-right":
                pixel_x = canvas_width - pixel_x - img.width   # x ←
                pixel_y = canvas_height - pixel_y - img.height  # y ↑

            canvas.paste(img, (pixel_x, pixel_y))
            print(f"Pasted {img_data['filename']} at ({pixel_x}, {pixel_y}) [{orientation}]")
        
        except Exception as e:
            print(f"Error processing {img_path}: {str(e)}, skipping.")
            continue
    
    canvas.save(output_file)
    print(f"Panoramic image saved as {output_file}")


if __name__ == "__main__":
    import tkinter as tk
    from tkinter import filedialog

    root = tk.Tk()
    root.withdraw()

    xml_file = filedialog.askopenfilename(
        title="Выберите XML файл",
        filetypes=[("XML files", "*.xml")]
    )

    if not xml_file:
        print("Файл не выбран. Выход.")
    else:
        image_folder = os.path.dirname(xml_file)
        output_file = os.path.join(image_folder, "panorama.jpg")

        if not os.path.exists(image_folder):
            print(f"Ошибка: Папка с изображениями {image_folder} не найдена.")
        else:
            orientation = ask_coordinate_system(root)
            stitch_images(xml_file, output_file, image_folder, orientation=orientation)
            input("\nНажмите Enter, чтобы выйти...")
