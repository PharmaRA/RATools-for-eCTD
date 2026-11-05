"""Pinned PDF content adapter. No source mutation or permanent file cleanup.

Invoked by the durable processing host; outputs remain unadopted until host checks
and explicit draft adoption. PyMuPDF is a separately installed AGPL/commercial
runtime dependency. Successful processing does not establish regulatory conformance.
"""
import argparse
import hashlib
import importlib.metadata
import json
from pathlib import Path
import re
import subprocess
import sys
import threading
import time
import uuid

import pymupdf

ENGINE = 'pymupdf-1.28.2-content-v1'
ENGLISH_MODEL_SHA256 = '7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2'
FONTS = {
    'Helvetica': ('helv', '366720e054067fa49d030f58005c739fc787dcc9727a9327a28767777791d706'),
    'Helvetica-Bold': ('hebo', 'd4ea80d1d0e29a1e950808009ca8f96fe07a0670b60b556f6b2cf0705337fa9f'),
    'Helvetica-Oblique': ('heit', 'fa6acc6f51b5935a6b7defbc3ead480cd4906e3b198930470335db9a9bff6590'),
    'Helvetica-BoldOblique': ('hebi', '9cdfc2d0947339dce76f99bdf9e2d9fe01e472a384cf683007e6a0bb35634623'),
    'Times-Roman': ('tiro', 'eb609a306b81357853ba91c2a3c47f3bba04f3a8cfdeedc733b1aa4af5d03d4c'),
    'Times-Bold': ('tibo', '219011281845fd509a0042c1b70bf98d49e0f562815a6b4a2549ef2ca14fa70a'),
    'Times-Italic': ('tiit', '9deed810d473df0bfe15f426b2e69e560bcb756b801a9cbcebdd5cd82519ffa6'),
    'Times-BoldItalic': ('tibi', '12b53139c10085e8fdc7d427ee00f49fa80ac7d0c578fd9ca9196c80e2cbf59f'),
    'Courier': ('cour', '2e8380cc08f257fb0b301079323a442918ec9d69582c95607da5543b362aa6d6'),
    'Courier-Bold': ('cobo', 'aba820052e08d413b94465af8f1401e942234b57ac091a9c0e0a1dc10bb3f091'),
    'Courier-Oblique': ('coit', 'b8bce31c935e0888435387d6ec610700afcc06a778433004e56348d374a3bf3a'),
    'Courier-BoldOblique': ('cobi', '9edb0d4b29b122a9f9f8098815ca99e5d3a00b8a106e3c659d66c5ccadde2c9e'),
}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def validate_limit(value):
    if type(value) is not int or value < 1:
        raise ValueError('INVALID_RESOURCE_LIMIT')


def read_bounded(path, limit, code):
    validate_limit(limit)
    chunks, length = [], 0
    with Path(path).open('rb') as stream:
        while chunk := stream.read(min(65536, limit - length + 1)):
            length += len(chunk)
            if length > limit:
                raise ValueError(code)
            chunks.append(chunk)
    return b''.join(chunks)


def hash_bounded(path, limit, code, *, include_length=False):
    validate_limit(limit)
    value, length = hashlib.sha256(), 0
    with Path(path).open('rb') as stream:
        while chunk := stream.read(min(65536, limit - length + 1)):
            length += len(chunk)
            if length > limit:
                raise ValueError(code)
            value.update(chunk)
    return (value.hexdigest(), length) if include_length else value.hexdigest()


def model_snapshot(tessdata, output_path):
    model = read_bounded(Path(tessdata) / 'eng.traineddata', 32 * 1024 * 1024, 'OCR_MODEL_LIMIT_EXCEEDED')
    if digest(model) != ENGLISH_MODEL_SHA256:
        raise ValueError('OCR_MODEL_VERSION_CHANGED')
    directory = output_path.with_name(output_path.name + '.ocr-model-' + uuid.uuid4().hex)
    directory.mkdir()
    with (directory / 'eng.traineddata').open('xb') as stream:
        stream.write(model)
    return directory


def font_program(document, xref, visited=None):
    visited = set() if visited is None else visited
    if xref in visited:
        raise ValueError('FONT_GRAPH_NOT_QUALIFIED')
    visited.add(xref)
    descriptor = document.xref_get_key(xref, 'FontDescriptor')
    if descriptor[0] == 'xref':
        descriptor_id = int(descriptor[1].split()[0])
        for key in ('FontFile', 'FontFile2', 'FontFile3'):
            program = document.xref_get_key(descriptor_id, key)
            if program[0] == 'xref':
                return document.xref_stream(int(program[1].split()[0])) or b''
    descendants = document.xref_get_key(xref, 'DescendantFonts')
    if descendants[0] == 'array':
        match = re.fullmatch(r'\[\s*(\d+)\s+0\s+R\s*\]', descendants[1])
        if not match:
            raise ValueError('FONT_GRAPH_NOT_QUALIFIED')
        return font_program(document, int(match[1]), visited)
    return b''


def verify_fonts(document, receipt):
    programs = []
    for xref in range(1, document.xref_length()):
        if document.xref_get_key(xref, 'Type') != ('name', '/Font'):
            continue
        program = font_program(document, xref)
        if not program:
            raise ValueError('FONT_EMBEDDING_INCOMPLETE')
        programs.append((document.xref_get_key(xref, 'BaseFont')[1].removeprefix('/'), digest(program), len(program)))
    for item in receipt['embedded']:
        expected = (item['name'], item.get('programSha256'), item.get('programBytes'))
        if expected not in programs:
            raise ValueError('FONT_EMBEDDING_INCOMPLETE')
        programs.remove(expected)


def probe():
    if pymupdf.VersionBind != '1.28.2':
        raise ValueError('CONTENT_ENGINE_VERSION_NOT_QUALIFIED')
    package = Path(pymupdf.__file__).resolve().parent
    paths = {Path(module.__file__).resolve() for name, module in tuple(sys.modules.items())
             if (name == 'pymupdf' or name.startswith('pymupdf.')) and getattr(module, '__file__', None)}
    distribution = importlib.metadata.distribution('PyMuPDF')
    # Native libraries loaded by the Python extension are also runtime inputs;
    # their exact package entries are read, without scanning installation folders.
    for entry in distribution.files or []:
        if entry.suffix.lower() in ('.pyd', '.dll', '.so') and entry.parts[0] == 'pymupdf':
            paths.add(Path(distribution.locate_file(entry)).resolve())
    if not paths or len(paths) > 64:
        raise ValueError('RUNTIME_FILE_LIMIT_EXCEEDED')
    value, total = hashlib.sha256(), 0
    for path in sorted(paths):
        if not path.is_relative_to(package) or path.suffix.lower() not in ('.py', '.pyd', '.dll', '.so'):
            raise ValueError('RUNTIME_PATH_NOT_QUALIFIED')
        remaining = 512 * 1024 * 1024 - total
        if remaining < 1:
            raise ValueError('RUNTIME_TOTAL_LIMIT_EXCEEDED')
        file_hash, length = hash_bounded(path, min(256 * 1024 * 1024, remaining), 'RUNTIME_FILE_LIMIT_EXCEEDED', include_length=True)
        total += length
        value.update(path.relative_to(package).as_posix().encode('utf-8') + b'\0')
        value.update(file_hash.encode('ascii'))
    return {'engineVersion': ENGINE, 'pythonVersion': sys.version.split()[0], 'pyMuPdfVersion': pymupdf.VersionBind,
            'runtimeFingerprint': value.hexdigest()}


def inspect(source_path, *, max_input_bytes=512 * 1024 * 1024, max_pixels=24000000):
    validate_limit(max_input_bytes)
    validate_limit(max_pixels)
    if pymupdf.VersionBind != '1.28.2':
        raise ValueError('CONTENT_ENGINE_VERSION_NOT_QUALIFIED')
    original = read_bounded(source_path, max_input_bytes, 'INPUT_LIMIT_EXCEEDED')
    receipt = {'engineVersion': ENGINE, 'sourceSha256': digest(original), 'pageCount': 0, 'sourceVersion': None,
               'searchablePages': [], 'scannedPages': [], 'missingFonts': [], 'blockReasons': []}
    try:
        with pymupdf.open(stream=original, filetype='pdf') as document:
            receipt['pageCount'] = document.page_count
            receipt['sourceVersion'] = (document.metadata or {}).get('format', '').removeprefix('PDF ') or None
            source_safety(document)
            for index, page in enumerate(document, 1):
                if (page.rect.width * 150 / 72 + 1) * (page.rect.height * 150 / 72 + 1) > max_pixels:
                    raise ValueError('PAGE_PIXEL_LIMIT_EXCEEDED')
                receipt['searchablePages' if page.get_text().strip() else 'scannedPages'].append(index)
            missing = {}
            for xref in range(1, document.xref_length()):
                if document.xref_get_key(xref, 'Type') != ('name', '/Font') or font_program(document, xref):
                    continue
                name = document.xref_get_key(xref, 'BaseFont')[1].removeprefix('/')
                qualified = name in FONTS and document.xref_get_key(xref, 'Subtype') == ('name', '/Type1') and document.xref_get_key(xref, 'Encoding') == ('name', '/WinAnsiEncoding')
                missing[name] = missing.get(name, True) and qualified
            receipt['missingFonts'] = [{'name': name, 'qualified': qualified} for name, qualified in sorted(missing.items())]
    except (ValueError, RuntimeError, pymupdf.FileDataError) as error:
        code = str(error)
        receipt['blockReasons'].append(code if re.fullmatch('[A-Z][A-Z_]+', code) else 'PDF_INSPECTION_FAILED')
    if hash_bounded(source_path, max_input_bytes, 'INPUT_LIMIT_EXCEEDED') != receipt['sourceSha256']:
        raise ValueError('SOURCE_CHANGED_DURING_INSPECTION')
    return receipt


def validate_options(options, page_count):
    if not isinstance(options, dict) or set(options) - {'ocr', 'embedStandardFonts', 'targetVersion'}:
        raise ValueError('PARAMETERS_NOT_QUALIFIED')
    if type(options.get('embedStandardFonts', False)) is not bool:
        raise ValueError('INVALID_FONT_OPTION')
    if options.get('targetVersion') not in (None, '1.7'):
        raise ValueError('VERSION_NOT_QUALIFIED')
    ocr = options.get('ocr')
    if ocr is not None:
        if not isinstance(ocr, dict) or set(ocr) != {'language', 'pages', 'dpi'}:
            raise ValueError('INVALID_OCR_OPTIONS')
        if ocr['language'] != 'eng' or ocr['dpi'] != 150:
            raise ValueError('OCR_CONFIGURATION_NOT_QUALIFIED')
        pages = ocr['pages']
        if not isinstance(pages, list) or not pages or any(type(p) is not int or p < 1 or p > page_count for p in pages):
            raise ValueError('INVALID_OCR_PAGES')
        if len(set(pages)) != len(pages) or pages != sorted(pages):
            raise ValueError('OCR_PAGES_MUST_BE_UNIQUE_AND_ORDERED')
    if ocr is None and not options.get('embedStandardFonts') and options.get('targetVersion') is None:
        raise ValueError('NO_CONTENT_OPERATION_SELECTED')


def source_safety(document):
    if not document.is_pdf or document.is_repaired:
        raise ValueError('PDF_REQUIRES_REPAIR_OR_IS_UNSUPPORTED')
    if document.needs_pass or document.is_encrypted or document.xref_get_key(-1, 'Encrypt')[0] != 'null':
        raise ValueError('ENCRYPTED_PDF_NOT_QUALIFIED')
    if document.xref_length() > 200000 or document.page_count < 1 or document.page_count > 500:
        raise ValueError('DOCUMENT_LIMIT_EXCEEDED')
    for xref in range(1, document.xref_length()):
        if document.xref_get_key(xref, 'Type') == ('name', '/Sig') or document.xref_get_key(xref, 'FT') == ('name', '/Sig'):
            raise ValueError('SIGNATURE_MODIFICATION_NOT_QUALIFIED')
        if document.xref_get_key(xref, 'S') == ('name', '/JavaScript'):
            raise ValueError('ACTIVE_CONTENT_NOT_QUALIFIED')
    if document.is_form_pdf:
        raise ValueError('FORM_PROCESSING_NOT_QUALIFIED')
    match = re.fullmatch(r'PDF (1\.[0-7])', document.metadata['format'])
    if not match:
        raise ValueError('SOURCE_VERSION_NOT_QUALIFIED')
    catalog_version = document.xref_get_key(document.pdf_catalog(), 'Version')
    if catalog_version[0] != 'null' and catalog_version[1] not in ['/1.' + str(value) for value in range(8)]:
        raise ValueError('SOURCE_VERSION_NOT_QUALIFIED')


def embed_standard_fonts(document):
    embedded = []
    for xref in range(1, document.xref_length()):
        if document.xref_get_key(xref, 'Type') != ('name', '/Font'):
            continue
        if font_program(document, xref):
            continue
        name = document.xref_get_key(xref, 'BaseFont')[1].removeprefix('/')
        if name not in FONTS or document.xref_get_key(xref, 'Subtype') != ('name', '/Type1') or document.xref_get_key(xref, 'Encoding') != ('name', '/WinAnsiEncoding'):
            raise ValueError('FONT_NOT_QUALIFIED: ' + name)
        font = pymupdf.Font(FONTS[name][0])
        font_hash = digest(font.buffer)
        if font_hash != FONTS[name][1]:
            raise ValueError('FONT_PROGRAM_VERSION_CHANGED')
        font_stream = document.get_new_xref()
        document.update_object(font_stream, '<</Subtype /Type1C>>')
        document.update_stream(font_stream, font.buffer)
        descriptor = document.get_new_xref()
        bbox = ' '.join(str(round(value * 1000)) for value in (font.bbox.x0, font.bbox.y0, font.bbox.x1, font.bbox.y1))
        italic = 'Italic' in name or 'Oblique' in name
        flags = 32 + (1 if name.startswith('Courier') else 0) + (2 if name.startswith('Times') else 0) + (64 if italic else 0)
        angle = (-15.5 if name == 'Times-Italic' else -15 if name == 'Times-BoldItalic' else -12) if italic else 0
        document.update_object(descriptor,
            f'<</Type /FontDescriptor /FontName /{name} /Flags {flags} /FontBBox [{bbox}] '
            f'/ItalicAngle {angle} /Ascent {round(font.ascender * 1000)} /Descent {round(font.descender * 1000)} '
            f'/CapHeight {round(font.glyph_bbox(ord("H")).y1 * 1000)} /StemV {120 if "Bold" in name else 80} /FontFile3 {font_stream} 0 R>>')
        widths = [round(font.glyph_advance(ord(bytes([code]).decode('cp1252', errors='replace'))) * 1000) for code in range(32, 256)]
        document.xref_set_key(xref, 'FontDescriptor', f'{descriptor} 0 R')
        document.xref_set_key(xref, 'FirstChar', '32')
        document.xref_set_key(xref, 'LastChar', '255')
        document.xref_set_key(xref, 'Widths', '[' + ' '.join(map(str, widths)) + ']')
        embedded.append({'name': name, 'programSha256': font_hash, 'programBytes': len(font.buffer)})
    return {'embedded': embedded, 'regulatoryConformance': 'NotEvaluated'}


def render(page, dpi, max_pixels):
    if (page.rect.width * dpi / 72 + 1) * (page.rect.height * dpi / 72 + 1) > max_pixels:
        raise ValueError('PAGE_PIXEL_LIMIT_EXCEEDED')
    return page.get_pixmap(dpi=dpi, colorspace=pymupdf.csRGB, alpha=False)


def add_ocr(document, options, tessdata, max_pixels):
    model = Path(tessdata) / 'eng.traineddata'
    if hash_bounded(model, 32 * 1024 * 1024, 'OCR_MODEL_LIMIT_EXCEEDED') != ENGLISH_MODEL_SHA256:
        raise ValueError('OCR_MODEL_VERSION_CHANGED')
    receipt = {'language': 'eng', 'dpi': 150, 'modelSha256': ENGLISH_MODEL_SHA256,
               'processedPages': [], 'alreadySearchablePages': [], 'requiresTextReview': True}
    for number in options['pages']:
        page = document[number - 1]
        if page.get_text().strip():
            receipt['alreadySearchablePages'].append(number)
            continue
        rotation = page.rotation
        page.set_rotation(0)
        pixmap = render(page, options['dpi'], max_pixels)
        with pymupdf.open(stream=pixmap.pdfocr_tobytes(language='eng', tessdata=str(tessdata)), filetype='pdf') as layer:
            if hash_bounded(model, 32 * 1024 * 1024, 'OCR_MODEL_LIMIT_EXCEEDED') != ENGLISH_MODEL_SHA256:
                raise ValueError('OCR_MODEL_VERSION_CHANGED')
            if not layer[0].get_text().strip():
                raise ValueError('OCR_NO_TEXT_RECOGNIZED')
            blocks = re.findall(rb'\bBT\s.*?\bET\b', layer[0].read_contents(), re.S)
            if not blocks or any(b'3 Tr' not in block for block in blocks):
                raise ValueError('OCR_LAYER_FORMAT_NOT_QUALIFIED')
            content = layer.get_new_xref()
            layer.update_object(content, '<<>>')
            layer.update_stream(content, b'\n'.join(blocks))
            layer[0].set_contents(content)
            page.show_pdf_page(page.rect, layer, 0, overlay=True)
        page.set_rotation(rotation)
        receipt['processedPages'].append(number)
    return receipt


def navigation(value):
    if isinstance(value, dict):
        return {key: navigation(item) for key, item in value.items() if key != 'xref'}
    if isinstance(value, (list, tuple)):
        return [navigation(item) for item in value]
    if isinstance(value, (pymupdf.Point, pymupdf.Rect)):
        return list(value)
    return value


def verify_fidelity(source, result, processed_pages, max_pixels):
    if len(source) != len(result) or navigation(source.get_toc(False)) != navigation(result.get_toc(False)) or navigation(source.resolve_names()) != navigation(result.resolve_names()):
        raise ValueError('NAVIGATION_OR_PAGE_COUNT_CHANGED')
    if source.pagemode != result.pagemode or source.pagelayout != result.pagelayout:
        raise ValueError('INITIAL_VIEW_CHANGED')
    pages = []
    for index, (before, after) in enumerate(zip(source, result), 1):
        if any(getattr(before, prop) != getattr(after, prop) for prop in ('mediabox', 'cropbox', 'trimbox', 'bleedbox', 'artbox', 'rotation')):
            raise ValueError('PAGE_GEOMETRY_CHANGED')
        if navigation(before.get_links()) != navigation(after.get_links()):
            raise ValueError('LINKS_CHANGED')
        if index not in processed_pages and before.get_text() != after.get_text():
            raise ValueError('EXISTING_TEXT_CHANGED')
        if index in processed_pages and not after.get_text().strip():
            raise ValueError('OCR_TEXT_MISSING')
        render_hashes = {}
        for dpi in (72, 150):
            old_hash = digest(render(before, dpi, max_pixels).samples)
            new_hash = digest(render(after, dpi, max_pixels).samples)
            if old_hash != new_hash:
                raise ValueError('VISIBLE_CONTENT_CHANGED')
            render_hashes[str(dpi)] = old_hash
        pages.append({'page': index, 'renderSha256': render_hashes, 'textSha256': digest(after.get_text().encode('utf-8'))})
    return pages


def normalize_version(data, qpdf, output_path, max_output_bytes):
    version = subprocess.run([str(qpdf), '--version'], check=True, capture_output=True, timeout=10).stdout.decode().splitlines()[0]
    if version != 'qpdf version 12.4.1':
        raise ValueError('QPDF_VERSION_NOT_QUALIFIED')
    stage = output_path.with_name(output_path.name + '.version-input-' + uuid.uuid4().hex + '.pdf')
    with stage.open('xb') as stream:
        stream.write(data)
    errors = stage.with_suffix('.stderr.txt')
    with errors.open('xb') as log:
        process_handle = subprocess.Popen([str(qpdf), str(stage), '--min-version=1.7', '--object-streams=preserve', '-'],
                                          stdout=subprocess.PIPE, stderr=log)
        expired = threading.Event()

        def timeout():
            expired.set()
            process_handle.kill()

        timer = threading.Timer(60, timeout)
        timer.start()
        try:
            chunks, length = [], 0
            while chunk := process_handle.stdout.read(65536):
                length += len(chunk)
                if length > max_output_bytes:
                    raise ValueError('OUTPUT_LIMIT_EXCEEDED')
                chunks.append(chunk)
            code = process_handle.wait()
            if expired.is_set():
                raise ValueError('VERSION_PROCESSING_TIMED_OUT')
            if code != 0:
                raise ValueError('VERSION_PROCESSING_FAILED')
            return b''.join(chunks)
        finally:
            timer.cancel()
            if process_handle.poll() is None:
                process_handle.kill()
            process_handle.wait()
            process_handle.stdout.close()


def process(source_path, output_path, options, *, tessdata=None, qpdf=None,
            max_input_bytes=512 * 1024 * 1024, max_output_bytes=512 * 1024 * 1024, max_pixels=24000000):
    for limit in (max_input_bytes, max_output_bytes, max_pixels):
        validate_limit(limit)
    started = time.monotonic()
    source_path, output_path = Path(source_path), Path(output_path)
    if output_path.exists() or output_path.is_symlink():
        raise FileExistsError('Processing output already exists')
    if pymupdf.VersionBind != '1.28.2':
        raise ValueError('CONTENT_ENGINE_VERSION_NOT_QUALIFIED')
    original = read_bounded(source_path, max_input_bytes, 'INPUT_LIMIT_EXCEEDED')
    with pymupdf.open(stream=original, filetype='pdf') as document:
        source_safety(document)
        validate_options(options, len(document))
        receipt = {'engineVersion': ENGINE, 'sourceSha256': digest(original), 'parameters': options,
                   'ocr': None, 'fonts': None, 'regulatoryConformance': 'NotEvaluated'}
        if options.get('ocr'):
            if tessdata is None:
                raise ValueError('OCR_MODEL_NOT_CONFIGURED')
            snapshot = model_snapshot(tessdata, output_path)
            receipt['ocr'] = add_ocr(document, options['ocr'], snapshot, max_pixels)
        if options.get('embedStandardFonts'):
            receipt['fonts'] = embed_standard_fonts(document)
        data = document.tobytes(garbage=0, clean=False, deflate=True)
    if len(data) > max_output_bytes:
        raise ValueError('OUTPUT_LIMIT_EXCEEDED')
    if options.get('targetVersion'):
        if qpdf is None:
            raise ValueError('QPDF_NOT_CONFIGURED')
        data = normalize_version(data, qpdf, output_path, max_output_bytes)
    with pymupdf.open(stream=original, filetype='pdf') as source, pymupdf.open(stream=data, filetype='pdf') as result:
        source_safety(result)
        if receipt['fonts'] is not None:
            verify_fonts(result, receipt['fonts'])
        processed_pages = receipt['ocr']['processedPages'] if receipt['ocr'] else []
        receipt['pages'] = verify_fidelity(source, result, processed_pages, max_pixels)
        receipt['outputVersion'] = result.metadata['format'].removeprefix('PDF ')
        if options.get('targetVersion') and receipt['outputVersion'] != options['targetVersion']:
            raise ValueError('OUTPUT_VERSION_MISMATCH')
    if hash_bounded(source_path, max_input_bytes, 'INPUT_LIMIT_EXCEEDED') != receipt['sourceSha256']:
        raise ValueError('SOURCE_CHANGED_DURING_PROCESSING')
    with output_path.open('xb') as stream:
        stream.write(data)
    receipt.update(outputSha256=digest(data), outputBytes=len(data), elapsedMilliseconds=round((time.monotonic() - started) * 1000))
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--request', type=Path, required=True)
    arguments = parser.parse_args()
    request = json.loads(read_bounded(arguments.request, 65536, 'REQUEST_LIMIT_EXCEEDED').decode('utf-8-sig'))
    receipt_path = Path(request['receiptPath'])
    if receipt_path.exists() or receipt_path.is_symlink():
        raise FileExistsError('Receipt already exists')
    mode = request.get('mode', 'process')
    if mode == 'probe':
        receipt = probe()
    elif mode == 'inspect':
        receipt = inspect(request['sourcePath'], max_input_bytes=request.get('maxInputBytes', 512 * 1024 * 1024),
                          max_pixels=request.get('maxPixels', 24000000))
    elif mode == 'process':
        receipt = process(request['sourcePath'], request['outputPath'], request['parameters'],
                          tessdata=request.get('tessdata'), qpdf=request.get('qpdf'),
                          max_input_bytes=request.get('maxInputBytes', 512 * 1024 * 1024),
                          max_output_bytes=request.get('maxOutputBytes', 512 * 1024 * 1024),
                          max_pixels=request.get('maxPixels', 24000000))
    else:
        raise ValueError('REQUEST_MODE_NOT_SUPPORTED')
    with receipt_path.open('x', encoding='utf-8') as stream:
        json.dump(receipt, stream, ensure_ascii=False, indent=2)
    print(json.dumps({'succeeded': True, 'engineVersion': ENGINE, 'outputSha256': receipt.get('outputSha256')}))


if __name__ == '__main__':
    main()
