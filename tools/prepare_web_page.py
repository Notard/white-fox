"""Add a responsive canvas and keyboard guide to Unity's default Web template."""
from pathlib import Path
root=Path(__file__).resolve().parents[1]
page=root/'Build'/'WebGL'/'index.html'
html=page.read_text(encoding='utf-8')
if 'white-fox-web-layout' not in html:
    css='''<style id="white-fox-web-layout">
html,body{margin:0;min-height:100%;background:#dbeefa;color:#26364a;font-family:system-ui,"Malgun Gothic",sans-serif}
body{padding:20px 0;box-sizing:border-box}
.fox-heading,.fox-controls{width:min(1280px,96vw);margin:0 auto;text-align:center}
.fox-heading h1{margin:0 0 6px;font-size:26px}.fox-heading p{margin:0 0 18px;font-size:15px}
#unity-container.unity-desktop,#unity-container.unity-mobile{position:relative!important;left:auto!important;top:auto!important;transform:none!important;width:min(1280px,96vw)!important;margin:auto}
#unity-canvas{display:block;width:100%!important;height:auto!important;aspect-ratio:16/9;border-radius:12px;background:#91bfd8}
#unity-footer{position:relative!important;min-height:38px}.fox-controls{padding-top:12px;font-size:14px;line-height:1.8}
@media(max-width:600px){body{padding-top:12px}.fox-heading h1{font-size:22px}.fox-controls{font-size:12px}}
</style>'''
    html=html.replace('</head>',css+'\n</head>').replace('<html lang="en-us">','<html lang="ko">')
    import re
    html=re.sub(r'<title>.*?</title>','<title>White Fox · 하얀 여우 퍼즐</title>',html)
    html=html.replace('<body>','<body>\n<header class="fox-heading"><h1>White Fox · 하얀 여우 퍼즐</h1><p>여우와 함께 섬을 건너 보석을 모아 보세요.</p></header>')
    html=html.replace('</body>','<p class="fox-controls">W A S D 이동 · Space 점프 · Q 기다리기 · R 다시 시작 · Esc 일시정지<br>마우스 휠로 확대·축소할 수 있어요.</p>\n</body>')
    page.write_text(html,encoding='utf-8')
print(page)
