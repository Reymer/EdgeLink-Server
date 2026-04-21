import os
import sys
import base64
import webview
import tkinter as tk
from tkinter import filedialog


def resource(filename):
    base = sys._MEIPASS if getattr(sys, 'frozen', False) else os.path.dirname(os.path.abspath(__file__))
    return os.path.join(base, filename)


class Api:
    def save_file(self, filename: str, content_b64: str):
        content = base64.b64decode(content_b64).decode('utf-8')
        root = tk.Tk()
        root.withdraw()
        root.attributes('-topmost', True)
        path = filedialog.asksaveasfilename(
            parent=root,
            title='儲存遮罩定義',
            initialfile=filename,
            defaultextension='.json',
            filetypes=[('JSON 檔案', '*.json'), ('所有檔案', '*.*')]
        )
        root.destroy()
        if path:
            with open(path, 'w', encoding='utf-8') as f:
                f.write(content)
            return True
        return False


if __name__ == '__main__':
    html_path = resource('MaskEditor.html')
    url = 'file:///' + html_path.replace('\\', '/')

    api = Api()
    window = webview.create_window(
        title='遮罩定義編輯器',
        url=url,
        js_api=api,
        width=1100,
        height=820,
        min_size=(800, 600),
        text_select=True,
    )
    webview.start(gui='edgechromium')
