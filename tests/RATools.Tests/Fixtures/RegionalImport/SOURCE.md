# Regional import external evidence

`ema-national-eu-m1-1.4.xml` is the unmodified regional XML at
`ex2-NPv1.0/0012/m1/eu/eu-regional.xml` in the EMA example archive:

https://esubmission.ema.europa.eu/eumodule1/docs/CorrectedExamples.zip

Acquired 2026-11-06. Archive SHA-256:
`759ee235ad547e55d4cd9cb8944e944bdeb81a326a1fcca51fda7860d2c525e1`.
XML SHA-256:
`c4e10a0f5cc78d15a2e851dadab83b1eeffea676583db86d3416efdf2cd9019a`.
The XML declares EU M1 DTD 1.4. It provides independently sourced repeated
names and country/language/PI contexts, and exercises explicit unsupported
version diagnostics. It does not qualify the current EU M1 3.1.1 profile.

The complete unmodified sequence, source manifest and archive are retained in
`.artifacts/p605-official/`. They are used for local external import acceptance;
the committed XML fixture keeps parser acceptance reproducible without a network
request. The independent HTTP/PostgreSQL fixture is hand-written and labelled
synthetic in its test; it is not an EMA example or regulatory qualification.
