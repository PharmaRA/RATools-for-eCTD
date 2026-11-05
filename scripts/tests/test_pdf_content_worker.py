"""Real PDF content processing; all task-owned artifacts are retained."""
import hashlib
import importlib.util
import os
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import patch
import uuid

ROOT = Path(__file__).resolve().parents[2]
ENABLED = os.environ.get('RATOOLS_PDF_CONTENT_TESTS') == '1'
if ENABLED:
    import pymupdf
    SPEC = importlib.util.spec_from_file_location('pdf_content_worker', ROOT / 'scripts/pdf_content_worker.py')
    worker = importlib.util.module_from_spec(SPEC)
    SPEC.loader.exec_module(worker)


@unittest.skipUnless(ENABLED, 'P5-08 NotEvaluated: set RATOOLS_PDF_CONTENT_TESTS=1 and configure qualified tools')
class PdfContentWorkerTests(unittest.TestCase):
    def setUp(self):
        self.directory = ROOT / '.artifacts/pdf-content-worker-tests' / uuid.uuid4().hex
        self.directory.mkdir(parents=True)
        self.tessdata = Path(os.environ['RATOOLS_TESSDATA'])
        self.qpdf = os.environ['RATOOLS_QPDF']
        self.source = self.directory / 'source.pdf'
        self.output = self.directory / 'output.pdf'

    def fixture(self, scanned=False, rotations=False):
        image = pymupdf.open()
        page = image.new_page(width=500, height=260)
        page.insert_text((50, 100), 'Clinical study report 12345', fontsize=20)
        page.insert_text((50, 150), 'Results and conclusions 67890', fontsize=20)
        document = pymupdf.open()
        for index in range(4 if rotations else 2):
            page = document.new_page(width=500, height=260)
            if scanned:
                page.insert_image(page.rect, pixmap=image[0].get_pixmap(dpi=150))
            else:
                page.insert_text((30, 100), f'Clinical study report {index + 1}', fontsize=20)
            if rotations:
                if index % 2:
                    page.set_rotation(90 if index == 1 else 270)
                if index > 1:
                    page.set_cropbox(pymupdf.Rect(20, 20, 480, 240))
        document.set_toc([[1, 'Clinical overview', 1], [2, 'Results', 2]])
        document[0].insert_link({'kind': pymupdf.LINK_GOTO, 'from': pymupdf.Rect(30, 30, 100, 50), 'page': 1})
        document.save(self.source)

    def execute(self, **options):
        return worker.process(self.source, self.output, options, tessdata=self.tessdata, qpdf=self.qpdf)

    def assert_preserved(self, before_bytes, allow_text=False):
        self.assertEqual(before_bytes, self.source.read_bytes())
        subprocess.run([self.qpdf, '--check', str(self.output)], check=True, capture_output=True, timeout=30)
        with pymupdf.open(self.source) as before, pymupdf.open(self.output) as after:
            self.assertEqual(before.get_toc(), after.get_toc())
            self.assertEqual(before.page_count, after.page_count)
            self.assertEqual(before[0].get_links()[0]['page'], after[0].get_links()[0]['page'])
            for old, new in zip(before, after):
                self.assertEqual(old.mediabox, new.mediabox)
                self.assertEqual(old.cropbox, new.cropbox)
                self.assertEqual(old.rotation, new.rotation)
                self.assertEqual(hashlib.sha256(old.get_pixmap(dpi=150).samples).hexdigest(),
                                 hashlib.sha256(new.get_pixmap(dpi=150).samples).hexdigest())
                if not allow_text:
                    self.assertEqual(old.get_text(), new.get_text())

    def test_ocr_selected_rotated_and_cropped_pages_retains_visuals_navigation_and_reading_order(self):
        self.fixture(scanned=True, rotations=True)
        original = self.source.read_bytes()
        receipt = self.execute(ocr={'language': 'eng', 'pages': [1, 2, 4], 'dpi': 150})
        self.assert_preserved(original, allow_text=True)
        with pymupdf.open(self.output) as result:
            for index in [0, 1, 3]:
                self.assertEqual(['Clinical study report 12345', 'Results and conclusions 67890'], result[index].get_text().splitlines())
            self.assertEqual('', result[2].get_text())
        self.assertEqual([1, 2, 4], receipt['ocr']['processedPages'])
        self.assertEqual(hashlib.sha256(original).hexdigest(), receipt['sourceSha256'])
        self.assertEqual(hashlib.sha256(self.output.read_bytes()).hexdigest(), receipt['outputSha256'])

    def test_ocr_skips_existing_searchable_text_without_duplicating_it(self):
        self.fixture()
        original = self.source.read_bytes()
        receipt = self.execute(ocr={'language': 'eng', 'pages': [1, 2], 'dpi': 150})
        self.assert_preserved(original)
        self.assertEqual([], receipt['ocr']['processedPages'])
        self.assertEqual([1, 2], receipt['ocr']['alreadySearchablePages'])

    def test_embeds_twelve_standard_winansi_fonts_and_preserves_text_pixels(self):
        names = ['helv', 'hebo', 'heit', 'hebi', 'tiro', 'tibo', 'tiit', 'tibi', 'cour', 'cobo', 'coit', 'cobi']
        document = pymupdf.open()
        for name in names:
            page = document.new_page(width=500, height=260)
            page.insert_text((30, 100), 'Clinical report “Café” – 123 €', fontsize=18, fontname=name)
        document.set_toc([[1, 'Fonts', 1]])
        document[0].insert_link({'kind': pymupdf.LINK_GOTO, 'from': pymupdf.Rect(30, 30, 100, 50), 'page': 1})
        document.save(self.source)
        original = self.source.read_bytes()
        receipt = self.execute(embedStandardFonts=True)
        self.assert_preserved(original)
        self.assertEqual(12, len(receipt['fonts']['embedded']))
        with pymupdf.open(self.output) as result:
            for page in result:
                self.assertTrue(all(len(result.extract_font(font[0])[3]) > 0 for font in page.get_fonts(full=True)))

    def test_unknown_missing_font_is_rejected_instead_of_claiming_embedding(self):
        self.fixture()
        with pymupdf.open(self.source) as document:
            font = document[0].get_fonts()[0][0]
            document.xref_set_key(font, 'BaseFont', '/UnqualifiedClinicalFont')
            alternative = self.directory / 'unknown-font.pdf'
            document.save(alternative)
        self.source = alternative
        with self.assertRaisesRegex(ValueError, 'FONT_NOT_QUALIFIED'):
            self.execute(embedStandardFonts=True)
        self.assertFalse(self.output.exists())

    def test_normalizes_older_pdf_with_real_writer_without_changing_content(self):
        self.fixture()
        older = self.directory / 'version-1.4.pdf'
        subprocess.run([self.qpdf, str(self.source), '--object-streams=disable', '--force-version=1.4', str(older)],
                       check=True, capture_output=True, timeout=30)
        self.source = older
        original = self.source.read_bytes()
        receipt = self.execute(targetVersion='1.7')
        self.assert_preserved(original)
        with pymupdf.open(self.output) as result:
            self.assertEqual('PDF 1.7', result.metadata['format'])
        self.assertEqual('1.7', receipt['outputVersion'])
        self.assertEqual('NotEvaluated', receipt['regulatoryConformance'])

    def test_invalid_page_language_version_and_unknown_parameters_fail_before_output(self):
        self.fixture(scanned=True)
        for options in [
            {'ocr': {'language': 'eng', 'pages': [3], 'dpi': 150}},
            {'ocr': {'language': 'chi_sim', 'pages': [1], 'dpi': 150}},
            {'ocr': {'language': 'eng', 'pages': [1], 'dpi': 72}},
            {'targetVersion': '1.4'}, {'repairEverything': True}, {},
        ]:
            with self.subTest(options=options), self.assertRaises(ValueError):
                self.execute(**options)
            self.assertFalse(self.output.exists())

    def test_existing_files_and_source_are_never_overwritten(self):
        self.fixture()
        self.output.write_bytes(b'retained existing result')
        with self.assertRaises(FileExistsError):
            self.execute(embedStandardFonts=True)
        self.assertEqual(b'retained existing result', self.output.read_bytes())
        original = self.source.read_bytes()
        self.output = self.source
        with self.assertRaises(FileExistsError):
            self.execute(embedStandardFonts=True)
        self.assertEqual(original, self.source.read_bytes())

    def test_unexpected_visible_change_fails_fidelity_verification(self):
        self.fixture()
        original = self.source.read_bytes()
        actual_embedding = worker.embed_standard_fonts

        def faulty_embedding(document):
            receipt = actual_embedding(document)
            document[0].draw_rect(pymupdf.Rect(10, 10, 100, 100), fill=(1, 0, 0))
            return receipt

        with patch.object(worker, 'embed_standard_fonts', faulty_embedding):
            with self.assertRaisesRegex(ValueError, 'VISIBLE_CONTENT_CHANGED'):
                self.execute(embedStandardFonts=True)
        self.assertEqual(original, self.source.read_bytes())

    def test_signature_and_encryption_are_rejected_before_processing(self):
        self.fixture()
        original_source = self.source
        signed = self.directory / 'signature.pdf'
        with pymupdf.open(original_source) as document:
            signature = document.get_new_xref()
            document.update_object(signature, '<</Type /Sig /ByteRange [0 1 2 3] /Contents <0000>>>')
            document.xref_set_key(document.pdf_catalog(), 'Perms', f'<</DocMDP {signature} 0 R>>')
            document.save(signed)
        encrypted = self.directory / 'encrypted.pdf'
        subprocess.run([self.qpdf, '--encrypt', '', '', '256', '--', str(original_source), str(encrypted)],
                       check=True, capture_output=True, timeout=30)
        for path in [signed, encrypted]:
            self.source = path
            with self.subTest(path=path.name), self.assertRaises(ValueError):
                self.execute(embedStandardFonts=True)
            self.assertFalse(self.output.exists())

    def test_font_operation_must_be_verified_on_actual_result(self):
        self.fixture()
        with patch.object(worker, 'embed_standard_fonts', return_value={'embedded': [{'name': 'Helvetica'}]}):
            with self.assertRaisesRegex(ValueError, 'FONT_EMBEDDING_INCOMPLETE'):
                self.execute(embedStandardFonts=True)
        self.assertFalse(self.output.exists())

    def test_input_output_and_render_limits_fail_without_publishing_output(self):
        self.fixture()
        for limits in [{'max_input_bytes': 1}, {'max_output_bytes': 1}, {'max_pixels': 1}]:
            with self.subTest(limits=limits), self.assertRaisesRegex(ValueError, 'LIMIT_EXCEEDED'):
                worker.process(self.source, self.output, {'embedStandardFonts': True}, **limits)
            self.assertFalse(self.output.exists())

    def test_ocr_and_embedding_can_run_together_without_losing_the_new_text_layer(self):
        self.fixture(scanned=True)
        original = self.source.read_bytes()
        receipt = self.execute(ocr={'language': 'eng', 'pages': [1, 2], 'dpi': 150}, embedStandardFonts=True, targetVersion='1.7')
        self.assert_preserved(original, allow_text=True)
        self.assertEqual([1, 2], receipt['ocr']['processedPages'])
        with pymupdf.open(self.output) as result:
            self.assertEqual('Clinical study report 12345', result[0].get_text().splitlines()[0])

    def test_ocr_uses_verified_model_snapshot_when_external_model_changes(self):
        self.fixture(scanned=True)
        model_directory = self.directory / 'external-model'
        model_directory.mkdir()
        model = model_directory / 'eng.traineddata'
        model.write_bytes((self.tessdata / 'eng.traineddata').read_bytes())
        self.tessdata = model_directory
        original_ocr = pymupdf.Pixmap.pdfocr_tobytes
        used_directories = []

        def replace_external_model(pixmap, *args, **kwargs):
            used_directories.append(Path(kwargs['tessdata']))
            model.write_bytes(b'changed external model')
            self.assertNotEqual(model_directory, Path(kwargs['tessdata']))
            return original_ocr(pixmap, *args, **kwargs)

        with patch.object(pymupdf.Pixmap, 'pdfocr_tobytes', replace_external_model):
            receipt = self.execute(ocr={'language': 'eng', 'pages': [1, 2], 'dpi': 150})
        self.assertEqual(2, len(used_directories))
        self.assertEqual(used_directories[0], used_directories[1])
        self.assertEqual(worker.ENGLISH_MODEL_SHA256, receipt['ocr']['modelSha256'])

    def test_stream_limits_apply_without_using_unbounded_path_reads(self):
        self.fixture()
        with patch.object(Path, 'read_bytes', side_effect=AssertionError('unbounded read')):
            receipt = self.execute(embedStandardFonts=True)
            with self.assertRaisesRegex(ValueError, 'INPUT_LIMIT_EXCEEDED'):
                worker.hash_bounded(self.source, 1, 'INPUT_LIMIT_EXCEEDED')
        self.assertEqual(hashlib.sha256(self.output.read_bytes()).hexdigest(), receipt['outputSha256'])

    def test_invalid_limits_are_rejected_before_opening_any_file(self):
        for value in [-2, -1, 0, 1.5, '100', True, None]:
            with self.subTest(value=value), patch.object(Path, 'open', side_effect=AssertionError('file opened')):
                for reader in [worker.read_bounded, worker.hash_bounded]:
                    with self.assertRaisesRegex(ValueError, 'INVALID_RESOURCE_LIMIT'):
                        reader(self.source, value, 'INPUT_LIMIT_EXCEEDED')
                for name in ['max_input_bytes', 'max_output_bytes', 'max_pixels']:
                    with self.assertRaisesRegex(ValueError, 'INVALID_RESOURCE_LIMIT'):
                        worker.process(self.source, self.output, {'embedStandardFonts': True}, **{name: value})

    def test_content_inspection_distinguishes_scanned_searchable_pages_and_missing_fonts(self):
        self.fixture(scanned=True)
        original = self.source.read_bytes()
        scanned = worker.inspect(self.source)
        self.assertEqual([1, 2], scanned['scannedPages'])
        self.assertEqual([], scanned['searchablePages'])
        self.assertEqual([], scanned['blockReasons'])
        self.assertEqual(hashlib.sha256(original).hexdigest(), scanned['sourceSha256'])
        self.assertEqual(original, self.source.read_bytes())
        self.source = self.directory / 'searchable.pdf'
        self.fixture()
        searchable = worker.inspect(self.source)
        self.assertEqual([1, 2], searchable['searchablePages'])
        self.assertEqual([{'name': 'Helvetica', 'qualified': True}], searchable['missingFonts'])
        self.assertFalse(self.output.exists())

    def test_content_inspection_reports_blockers_without_producing_pdf(self):
        self.fixture()
        result = worker.inspect(self.source, max_pixels=1)
        self.assertIn('PAGE_PIXEL_LIMIT_EXCEEDED', result['blockReasons'])
        self.assertFalse(self.output.exists())

    def test_runtime_probe_returns_stable_digest_without_exposing_paths(self):
        first, second = worker.probe(), worker.probe()
        self.assertEqual(first, second)
        self.assertEqual('1.28.2', first['pyMuPdfVersion'])
        self.assertEqual(worker.ENGINE, first['engineVersion'])
        self.assertRegex(first['runtimeFingerprint'], '^[a-f0-9]{64}$')
        self.assertNotIn(str(ROOT), str(first))

    def test_inspection_cli_writes_exclusive_receipt_without_result_file(self):
        self.fixture()
        request = self.directory / 'inspect-request.json'
        receipt = self.directory / 'inspection.json'
        import json
        request.write_text(json.dumps({'mode': 'inspect', 'sourcePath': str(self.source), 'receiptPath': str(receipt)}), encoding='utf-8')
        command = [sys.executable, '-B', str(ROOT / 'scripts/pdf_content_worker.py'), '--request', str(request)]
        result = subprocess.run(command, check=True, capture_output=True, timeout=30)
        self.assertTrue(json.loads(result.stdout)['succeeded'])
        self.assertEqual(2, json.loads(receipt.read_text(encoding='utf-8'))['pageCount'])
        before = receipt.read_bytes()
        replay = subprocess.run(command, check=False, capture_output=True, timeout=30)
        self.assertNotEqual(0, replay.returncode)
        self.assertEqual(before, receipt.read_bytes())
        self.assertFalse(self.output.exists())


if __name__ == '__main__':
    unittest.main()
