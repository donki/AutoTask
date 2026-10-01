"""Genera el logo, el icono .ico y las imagenes del paquete MSIX y de la Store de sOC AutoTask.

Dibujo plano (constitucion 6.2 y E.6): cuadrado redondeado indigo #3525CD y, en blanco, una flecha
circular de repetir alrededor de un triangulo de reproducir. Se dibuja a 4x y se reduce.
Uso: python tools/logos.py
"""
import math
import os
from PIL import Image, ImageDraw

INDIGO = (53, 37, 205, 255)
WHITE = (255, 255, 255, 255)
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def glyph(draw, cx, cy, r, width):
    # Flecha circular (300 grados) con punta, y triangulo de reproducir dentro.
    box = [cx - r, cy - r, cx + r, cy + r]
    draw.arc(box, start=-40, end=215, fill=WHITE, width=width)
    # punta de la flecha al final del arco (230 grados)
    a = math.radians(215)
    ex, ey = cx + r * math.cos(a), cy + r * math.sin(a)
    tangent = a + math.pi / 2
    size = width * 1.45
    p1 = (ex + size * math.cos(tangent), ey + size * math.sin(tangent))
    p2 = (ex + size * math.cos(a) * 0.95, ey + size * math.sin(a) * 0.95)
    p3 = (ex - size * math.cos(a) * 0.95, ey - size * math.sin(a) * 0.95)
    draw.polygon([p1, p2, p3], fill=WHITE)
    t = r * 0.52
    draw.polygon([(cx - t * 0.55, cy - t), (cx - t * 0.55, cy + t), (cx + t * 0.95, cy)], fill=WHITE)


def logo(size, background=True, pad=0.0):
    s = size * 4
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    inset = int(s * pad)
    if background:
        d.rounded_rectangle([inset, inset, s - 1 - inset, s - 1 - inset], radius=int((s - 2 * inset) * 0.22), fill=INDIGO)
    inner = s - 2 * inset
    glyph(d, s / 2, s / 2, inner * 0.30, max(4, int(inner * 0.075)))
    return img.resize((size, size), Image.LANCZOS)


def canvas(w, h, scale=0.62):
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    side = int(min(w, h) * scale)
    img.alpha_composite(logo(side), ((w - side) // 2, (h - side) // 2))
    return img


def main():
    app = os.path.join(ROOT, "src", "sOCAutoTask")
    logo(256).save(os.path.join(app, "Assets", "logo.png"))
    big = logo(256)
    big.save(os.path.join(app, "appicon.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    player = os.path.join(ROOT, "src", "sOCAutoTaskPlayer")
    big.save(os.path.join(player, "appicon.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (256, 256)])

    pkg = os.path.join(ROOT, "Package", "Images")
    os.makedirs(pkg, exist_ok=True)
    logo(50).save(os.path.join(pkg, "StoreLogo.png"))
    logo(44).save(os.path.join(pkg, "Square44x44Logo.png"))
    logo(24).save(os.path.join(pkg, "Square44x44Logo.targetsize-24_altform-unplated.png"))
    canvas(150, 150, 0.7).save(os.path.join(pkg, "Square150x150Logo.png"))
    canvas(310, 310, 0.7).save(os.path.join(pkg, "Square310x310Logo.png"))
    canvas(310, 150, 0.7).save(os.path.join(pkg, "Wide310x150Logo.png"))
    canvas(71, 71, 0.8).save(os.path.join(pkg, "SmallTile.png"))
    logo(300).save(os.path.join(pkg, "StoreDisplay300.png"))

    store = os.path.join(ROOT, "store", "microsoft", "logos")
    os.makedirs(store, exist_ok=True)
    for n in (300, 150, 71):
        logo(n).save(os.path.join(store, f"icono-{n}x{n}.png"))
    for w, h in ((1080, 1080), (2160, 2160)):
        bg = Image.new("RGBA", (w, h), INDIGO)
        side = int(w * 0.6)
        bg.alpha_composite(logo(side, background=False), ((w - side) // 2, (h - side) // 2))
        bg.save(os.path.join(store, f"caja-1x1-{w}x{h}.png"))
    for w, h in ((720, 1080), (1440, 2160)):
        bg = Image.new("RGBA", (w, h), INDIGO)
        side = int(w * 0.7)
        bg.alpha_composite(logo(side, background=False), ((w - side) // 2, (h - side) // 2))
        bg.save(os.path.join(store, f"poster-9x16-{w}x{h}.png"))
    print("logos generados")


if __name__ == "__main__":
    main()
