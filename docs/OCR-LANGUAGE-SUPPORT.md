# FileLens Arabic & English OCR Language Specification

This document details the linguistic handling, normalization rules, bidirectional text support, and evaluation metrics for Arabic and English text recognized by the FileLens OCR engine.

## 1. Bilingual Design Principles

FileLens treats Arabic and English as first-class languages in OCR recognition and search indexing:
1. **Preserve Original Recognized Text:** The exact recognized characters, accents, Tashkeel diacritics, and layout positions are stored verbatim for document preview and fidelity.
2. **Normalized Search Indexing:** A separate search-normalized representation is passed through `TextNormalizer` to guarantee that queries match regardless of diacritics, spelling variations, or typing conventions.
3. **Provenance & Confidence:** Every recognized line and word retains a confidence score between 0.0 and 1.0 and a bounding box relative to the original image dimensions.

---

## 2. Arabic Linguistic Normalization Rules

When recognized Arabic text is indexed into SQLite FTS5:

### A. Diacritics (Tashkeel) Stripping
Arabic diacritical marks are stripped from the search representation:
- Fatha (`َ`), Damma (`ُ`), Kasra (`ِ`)
- Tanwin Fath (`ً`), Tanwin Damm (`ٌ`), Tanwin Kasr (`ٍ`)
- Sukun (`ْ`), Shadda (`ّ`)

*Example:* `تَقْرِيرُ` $\to$ `تقرير`

### B. Alef Letter Harmonization
Variations of Alef are mapped to a bare Alef (`ا`):
- `أ` (Alef with Hamza above) $\to$ `ا`
- `إ` (Alef with Hamza below) $\to$ `ا`
- `آ` (Alef with Madda above) $\to$ `ا`
- `ٱ` (Alef Wasla) $\to$ `ا`

*Example:* `إدارة الأرباح` $\to$ `ادارة الارباح`

### C. Taa Marbuta and Haa
- `ة` (Taa Marbuta) $\to$ `ه` (Haa)

*Example:* `الشركة` $\to$ `الشركه`

### D. Alef Maksura and Yaa
- `ى` (Alef Maksura) $\to$ `ي` (Yaa)

*Example:* `مستشفى` $\to$ `مستشفي`

### E. Tatweel / Kashida Removal
Decorative elongation bars (`ـ`) inserted by calligraphic justification are removed:
- `شــــركـة` $\to$ `شركة` $\to$ `شركه`

### F. Eastern & Western Arabic Numerals
Numerals are mapped to standard digits for consistent querying:
- `٠` $\to$ `0`, `١` $\to$ `1`, `٢` $\to$ `2`, `٣` $\to$ `3`, `٤` $\to$ `4`
- `٥` $\to$ `5`, `٦` $\to$ `6`, `٧` $\to$ `7`, `٨` $\to$ `8`, `٩` $\to$ `9`

---

## 3. Bidirectional (BiDi) & Mixed-Language Layouts

Documents often contain mixed Arabic and English in single paragraphs, tables, or invoices:
- **RTL Baseline:** Arabic flows right-to-left.
- **LTR Runs:** English terms, URLs, technical identifiers, and model numbers inside Arabic sentences flow left-to-right.
- **Table Grids:** Invoices with Arabic labels and English item descriptions/codes are extracted cell by cell with spatial coordinates preserved.
- **Bounding Boxes:** Each word and line bounding box contains `[X, Y, Width, Height]` normalized coordinates $(0.0 - 1.0)$ enabling precise visual highlighting on the preview panel.

---

## 4. Evaluation Metrics: CER & WER

To objectively measure OCR quality against ground-truth text, FileLens evaluates:

### Character Error Rate (CER)
$$\text{CER} = \frac{S + D + I}{N}$$
Where:
- $S$ = Number of character substitutions
- $D$ = Number of character deletions
- $I$ = Number of character insertions
- $N$ = Total number of characters in the ground-truth text

### Word Error Rate (WER)
$$\text{WER} = \frac{S_w + D_w + I_w}{N_w}$$
Where:
- $S_w, D_w, I_w$ are word-level substitutions, deletions, and insertions via Levenshtein distance on tokenized sequences.
- $N_w$ is the total number of words in ground-truth.

### Quality Thresholds
- **High Quality (Production Acceptance):** $\text{CER} < 5\%$, $\text{WER} < 10\%$
- **Acceptable (Scanned Documents):** $\text{CER} < 10\%$, $\text{WER} < 18\%$
- **Low Confidence Flag:** Recognized text with overall confidence $< 0.50$ is flagged with a warning badge in search results.

