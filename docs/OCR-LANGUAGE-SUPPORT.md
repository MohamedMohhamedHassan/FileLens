# FileLens Arabic and English OCR Language Support

## Production configuration

- Arabic language code: `ar`.
- Primary Arabic recognition model: `arabic_PP-OCRv5_mobile_rec`.
- Arabic model supports Arabic, Persian, Uyghur, Urdu, Pashto, Kurdish, Sindhi, Balochi, and English according to the official PaddleOCR 3.x model table.
- The installed PaddleOCR 3.7 English default resolves to `PP-OCRv6_medium_det` + `PP-OCRv6_medium_rec`.
- Official PP-OCRv5 English candidate: `en_PP-OCRv5_mobile_rec`; it is not reported active until its weight is downloaded and verified locally.

## Stored text and search normalization

FileLens preserves raw OCR text separately from normalized search text. Arabic normalization removes tashkeel and tatweel, harmonizes common Alef forms, maps Alef Maksura to Yaa, and normalizes Eastern Arabic numerals. English is case-folded for search while raw output remains available for preview. Each result retains language, confidence, line boxes, source file ID, and PDF page number where applicable.

Mixed RTL Arabic and LTR English/numeric runs are kept in the OCR line order returned by PaddleOCR; the preview uses normalized coordinates for highlighting.

## Metrics

CER is character-level Levenshtein distance divided by ground-truth characters. WER is token-level edit distance divided by ground-truth words. The verification suite reports Arabic and English independently and does not merge confidence scores into a probability.

The synthetic Phase 3 run produced Arabic CER 17.4% on the clean Arabic case and 22.2% on the diacritics case; English CER was 0.0% on the clean English case and 2.9% on the low-resolution case. These are verification fixtures, not a universal accuracy guarantee.
