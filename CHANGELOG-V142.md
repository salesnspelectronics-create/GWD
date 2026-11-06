# V142-R — حفظ خطای اصلی هنگام آزادسازی تلاش استقرار

خطای Stage/Save یا diagnostic در Dispose دیگر استثنای اصلی استقرار را نمی‌پوشاند؛ آزادسازی هر lease مستقل انجام می‌شود و خرابی یک مورد مانع تلاش برای بستن بقیه نمی‌شود. markerها در Dispose حذف نمی‌شوند.

- Failure injection for journal I/O and lease disposal is PENDING_USER_RUNTIME.
- Remote transactional implementation remains OPEN; safety block is retained.

STATIC_SOURCE_REVIEW=PARTIAL; AUTOMATED_TESTS=NOT_RUN; BUILD_VALIDATION=NOT_RUN; RUNTIME_REVIEW=PENDING_USER; TESTING_OWNER=USER.
