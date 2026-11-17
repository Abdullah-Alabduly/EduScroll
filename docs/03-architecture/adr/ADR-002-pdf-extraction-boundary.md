# ADR-002: PDF Text Extraction Boundary (`IPdfTextExtractor` Conflict)

- **Status:** PROPOSED — Pending Product Owner Approval (`TBD-002`, `CONF-002`)
- **Date:** 2026-11-17

## 1. Decision Description
Non-Negotiable Constraints #3 and #4 state that PDF text extraction happens locally in Flutter and extracted text is sent to the backend. However, `src/EduScroll.Application/Interfaces/Pdf/IPdfTextExtractor.cs` already exists in the backend repository (`ExtractAsync(Stream PdfStream, CancellationToken)`).

## 2. Why It Matters
Duplicating or silently moving PDF extraction to the backend violates Constraints #3/#4, introduces raw binary file upload/storage, increases backend memory/DoS exposure (malicious PDF parser exploits), and changes Sprint 4 API contracts.

## 3. Alternatives Considered
1. **Option A — Enforce Flutter-local PDF extraction only for MVP; remove/deprecate `IPdfTextExtractor` from backend:** Strictly follows Constraints #3/#4 and minimizes server attack surface and bandwidth.
2. **Option B — Move PDF extraction to the backend using `IPdfTextExtractor`:** Violates Constraint #3 unless Product Owner explicitly changes the constraint.
3. **Option C — Support both client and server extraction in MVP:** Violates Constraint #15 (focused MVP) and requires dual API/security surfaces.

## 4. Trade-Offs
- **Option A** preserves privacy (raw PDF never leaves device, `SEC-013`), saves server CPU/bandwidth, and honors Constraints #3/#4. Limitation: scanned/image-only PDFs cannot be extracted unless on-device OCR is added (`ASM-003`, `TBD-006`).
- **Option B** allows server-side OCR/heavy PDF libraries later, but violates Constraint #3 and introduces PDF parser vulnerability surface on the server.

## 5. Recommendation
**PROPOSED:** Adopt **Option A** for MVP—keep PDF text extraction strictly on Flutter and remove the unused `IPdfTextExtractor` stub in Sprint 1 (or explicitly archive it for post-MVP server OCR if ever requested).

## 6. Approval Status
**Pending Product Owner Approval (`TBD-002`).**
