"""Real qpdf output checked independently by MuPDF. No filesystem cleanup/deletion."""
import hashlib
import importlib.util
import os
from pathlib import Path
import subprocess
import unittest
import uuid

if os.environ.get("RATOOLS_QPDF"):
    import pymupdf

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("navigation_trial", ROOT / "scripts/qualify_pdf_navigation.py")
trial = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(trial)
SOURCE = ROOT / "tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf"


class NavigationQualificationTests(unittest.TestCase):
    def setUp(self):
        if not os.environ.get("RATOOLS_QPDF"):
            self.skipTest("P5-05 NotEvaluated: set RATOOLS_QPDF and install the qualification requirements")
        self.qpdf = os.environ["RATOOLS_QPDF"]
        self.directory = ROOT / ".artifacts/pdf-navigation-qualification" / uuid.uuid4().hex
        self.directory.mkdir(parents=True)

    def test_navigation_edits_preserve_source_page_streams_text_and_pixels(self):
        original = SOURCE.read_bytes()
        output = self.directory / "navigation.pdf"
        (self.directory / "renamed.pdf").write_bytes(original)
        trial.transform_trial(self.qpdf, SOURCE, output)
        self.assertEqual(original, SOURCE.read_bytes())
        with pymupdf.open(SOURCE) as before, pymupdf.open(output) as after:
            self.assertEqual([[1, "总览", 2], [2, "详细资料", 1]], after.get_toc())
            self.assertEqual("UseOutlines", after.pagemode)
            self.assertEqual(1, after.resolve_names()["overview"]["page"])
            internal, remote = after[0].get_links()
            self.assertEqual(pymupdf.LINK_GOTO, internal["kind"])
            self.assertEqual(1, internal["page"])
            # MuPDF 1.28 reports a remote named destination as a file URI;
            # inspect its PDF action as well, independently of qpdf's JSON.
            self.assertEqual("renamed.pdf#nameddest=overview", remote["file"])
            self.assertEqual(("name", "/GoToR"), after.xref_get_key(remote["xref"], "A/S"))
            self.assertEqual(("string", "renamed.pdf"), after.xref_get_key(remote["xref"], "A/F"))
            self.assertEqual(("string", "overview"), after.xref_get_key(remote["xref"], "A/D"))
            self.assertEqual("https://example.org/reference", after[1].get_links()[0]["uri"])
            self.assertEqual(2, after.page_count)
            for old, new in zip(before, after):
                self.assertEqual(old.get_text(), new.get_text())
                self.assertEqual([before.xref_stream(x) for x in old.get_contents()],
                                 [after.xref_stream(x) for x in new.get_contents()])
                self.assertEqual(hashlib.sha256(old.get_pixmap().samples).hexdigest(),
                                 hashlib.sha256(new.get_pixmap().samples).hexdigest())
        with pymupdf.open(self.directory / "renamed.pdf") as target:
            self.assertEqual(0, target.resolve_names()["overview"]["page"])

    def test_existing_output_and_source_are_never_overwritten(self):
        output = self.directory / "existing.pdf"
        output.write_bytes(b"retained user content")
        with self.assertRaises(FileExistsError):
            trial.transform_trial(self.qpdf, SOURCE, output)
        self.assertEqual(b"retained user content", output.read_bytes())
        with self.assertRaises(FileExistsError):
            trial.transform_trial(self.qpdf, SOURCE, SOURCE)

    def test_encrypted_input_is_rejected_without_output(self):
        encrypted = self.directory / "encrypted.pdf"
        subprocess.run([self.qpdf, "--encrypt", "", "", "256", "--", str(SOURCE), str(encrypted)],
                       check=True, capture_output=True, timeout=30)
        output = self.directory / "rejected.pdf"
        with self.assertRaisesRegex(ValueError, "Encrypted"):
            trial.transform_trial(self.qpdf, encrypted, output)
        self.assertFalse(output.exists())

    def test_signature_dictionary_is_rejected_without_output(self):
        signed = self.directory / "signature-dictionary.pdf"
        with pymupdf.open(SOURCE) as document:
            signature = document.get_new_xref()
            document.update_object(signature, "<< /Type /Sig /ByteRange [0 1 2 3] /Contents <0000> >>")
            document.xref_set_key(document.pdf_catalog(), "Perms", f"<< /DocMDP {signature} 0 R >>")
            document.save(signed)
        output = self.directory / "rejected.pdf"
        with self.assertRaisesRegex(ValueError, "Signature"):
            trial.transform_trial(self.qpdf, signed, output)
        self.assertFalse(output.exists())

    def test_timeout_terminates_qpdf_before_output(self):
        output = self.directory / "timed-out.pdf"
        with self.assertRaises(subprocess.TimeoutExpired):
            trial.transform_trial(self.qpdf, SOURCE, output, timeout=0.000000001)
        self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
