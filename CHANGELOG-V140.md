# V140-R — اعتبارسنجی مالکیت و سلامت بکاپ دستی و حفظ شواهد بازیابی

بکاپ CLI با manifest مستقل مالکیت پروژه/مقصد و inventory پوشه‌ها/SHA256 تکمیل می‌شود. Rollback پیش از STOP و پیش از جایگزینی، manifest و payload را بررسی می‌کند؛ خروجی restore نیز اعتبارسنجی می‌شود. بکاپ قدیمی بدون manifest خودکار معتبر تلقی نمی‌شود. نام پوشه بکاپ از خروج از ریشه محافظت می‌شود. اصل جابه‌جاشده و failed release به‌جای حذف حفظ می‌شوند.

- Legacy backups without a completion manifest are rejected for automatic/manual CLI restore; do not fabricate sidecars.
- CLI/IIS state restoration, corrupted backups and failure injection require user runtime validation.
- Manifests establish local provenance/integrity, not protection against malicious administrative tampering.

STATIC_SOURCE_REVIEW=PARTIAL; AUTOMATED_TESTS=NOT_RUN; BUILD_VALIDATION=NOT_RUN; RUNTIME_REVIEW=PENDING_USER; TESTING_OWNER=USER.
