# V116-R — حفظ فایل‌های اصلی تا تکمیل Resolve در Rollback CLI

- رفع ترتیب پرخطر حذف پوشه نسخه پیشین قبل از `ResolveIisAttempt` در مسیر موفق Rollback.
- اگر ثبت وضعیت موفقیت یا پاک‌سازی marker خطا دهد، نسخه پیشین برای مسیر بازیابی هنوز موجود است.
- پاک‌سازی نسخه پیشین پس از ثبت موفقیت، همچنان best-effort باقی می‌ماند.
- منطق توقف، کپی، پیکربندی و بررسی سلامت IIS تغییر نکرد.

## اعتبارسنجی

Static source review: COMPLETED. Automated tests and build: NOT_RUN — USER_TESTING_POLICY. Runtime review: PENDING_USER.
