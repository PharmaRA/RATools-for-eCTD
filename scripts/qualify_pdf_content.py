"""Produce retained P5-08 capability samples; requires an explicitly new directory."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

import pymupdf

from pdf_content_worker import FONTS, process, probe


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def version_line(executable):
    result = subprocess.run([str(executable), '-v'], check=False, capture_output=True, timeout=10)
    output = (result.stdout + result.stderr).decode('utf-8', errors='replace').splitlines()
    if not output:
        raise ValueError(f'No version output from {executable}')
    return output[0]


def run(output_dir, qpdf, tessdata, pdftotext):
    output_dir.mkdir(parents=True, exist_ok=False)
    receipt = {'runtime': probe(), 'qpdf': subprocess.run([str(qpdf), '--version'], check=True, capture_output=True, timeout=10).stdout.decode().splitlines()[0],
               'textOracle': version_line(pdftotext),
               'samples': [], 'regulatoryConformance': 'NotEvaluated'}
    text = ['Clinical study report 12345', 'Results and conclusions 67890']
    with pymupdf.open() as image, pymupdf.open() as scanned:
        for line in text:
            page = image.new_page(width=500, height=260)
            page.insert_text((50, 100), line, fontsize=20)
        for index in range(4):
            page = scanned.new_page(width=500, height=260)
            page.insert_image(page.rect, pixmap=image[index % 2].get_pixmap(dpi=150))
            if index > 1:
                page.set_cropbox(pymupdf.Rect(20, 20, 480, 240))
            if index % 2:
                page.set_rotation(90 if index == 1 else 270)
        scanned.set_toc([[1, 'Clinical overview', 1], [2, 'Conclusions', 2]])
        scanned[0].insert_link({'kind': pymupdf.LINK_GOTO, 'from': pymupdf.Rect(30, 30, 120, 50), 'page': 1})
        scanned.save(output_dir / 'scanned-source.pdf')
    with pymupdf.open() as fonts:
        for name, (alias, _) in FONTS.items():
            page = fonts.new_page(width=500, height=260)
            page.insert_text((30, 80), 'Clinical report “Café” – 123 €', fontsize=18, fontname=alias)
            page.insert_text((30, 130), name, fontsize=16, fontname=alias)
        fonts.set_toc([[1, 'Standard fonts', 1]])
        fonts[0].insert_link({'kind': pymupdf.LINK_GOTO, 'from': pymupdf.Rect(30, 30, 120, 50), 'page': 1})
        fonts.save(output_dir / 'fonts-source.pdf')
    # This generated file only uses basic text, pages and GoTo links, with no
    # post-1.4 features. The lower version is a qualification input, not a
    # production downgrade feature.
    subprocess.run([str(qpdf), str(output_dir / 'fonts-source.pdf'), '--object-streams=disable', '--force-version=1.4',
                    str(output_dir / 'version-source.pdf')], check=True, capture_output=True, timeout=30)
    samples = [('scanned', {'ocr': {'language': 'eng', 'pages': [1, 2, 4], 'dpi': 150}}),
               ('fonts', {'embedStandardFonts': True}), ('version', {'targetVersion': '1.7'})]
    for name, parameters in samples:
        source, output = output_dir / f'{name}-source.pdf', output_dir / f'{name}-result.pdf'
        before_hash = sha(source)
        result = process(source, output, parameters, qpdf=qpdf, tessdata=tessdata)
        assert sha(source) == before_hash
        syntax = subprocess.run([str(qpdf), '--check', str(output)], check=True, capture_output=True, timeout=30)
        independent_text = subprocess.run([str(pdftotext), '-layout', '-enc', 'UTF-8', str(output), '-'],
                                          check=True, capture_output=True, timeout=30).stdout.decode('utf-8')
        if name == 'scanned':
            pages = independent_text.split('\f')
            assert text[0] in pages[0] and text[1] in pages[1] and not pages[2].strip() and text[1] in pages[3]
        else:
            original_text = subprocess.run([str(pdftotext), '-layout', '-enc', 'UTF-8', str(source), '-'],
                                           check=True, capture_output=True, timeout=30).stdout.decode('utf-8')
            assert independent_text == original_text
        with (output_dir / f'{name}-receipt.json').open('x', encoding='utf-8') as stream:
            json.dump(result, stream, ensure_ascii=False, indent=2)
        with (output_dir / f'{name}-text.txt').open('x', encoding='utf-8') as stream:
            stream.write(independent_text)
        receipt['samples'].append({'name': name, 'source': source.name, 'sourceSha256': before_hash,
            'output': output.name, 'outputSha256': sha(output), 'parameters': parameters, 'syntaxExitCode': syntax.returncode,
            'textSha256': hashlib.sha256(independent_text.encode()).hexdigest(), 'fidelity': result['pages']})
    with (output_dir / 'qualification.json').open('x', encoding='utf-8') as stream:
        json.dump(receipt, stream, ensure_ascii=False, indent=2)
    return receipt


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output-dir', type=Path, required=True)
    parser.add_argument('--qpdf', type=Path, required=True)
    parser.add_argument('--tessdata', type=Path, required=True)
    parser.add_argument('--pdftotext', type=Path, required=True)
    args = parser.parse_args()
    result = run(args.output_dir, args.qpdf, args.tessdata, args.pdftotext)
    print(json.dumps({'samples': len(result['samples']), 'runtime': result['runtime'], 'regulatoryConformance': 'NotEvaluated'}))
