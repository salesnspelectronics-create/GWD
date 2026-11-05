# V114-R — رفع برخورد نام مسیر سایت پیشین هنگام Rollback CLI

- مسیر `.__cli_failed_` پیش‌تر فقط زمان با دقت ثانیه داشت؛ اجرای مجدد در همان ثانیه یا باقی‌ماندن پوشه از تلاش قبلی می‌توانست `Directory.Move` را پیش از بازیابی متوقف کند.
- GUID یکتا به انتهای مسیر افزوده شد تا هر تلاش، پوشه نگهداری مستقل داشته باشد.
- منطق توقف IIS، انتقال فایل، Health Check و بازیابی پس از شکست تغییر نکرد.

## Validation
- Static source review: COMPLETED
- AUTOMATED_TESTS=NOT_RUN — USER_TESTING_POLICY
- BUILD_VALIDATION=NOT_RUN — USER_TESTING_POLICY
- RUNTIME_REVIEW=PENDING_USER
- TESTING_OWNER=USER
