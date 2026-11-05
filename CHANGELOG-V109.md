# V109-R — بازانتشار ایمن نسخه یکسان

- رفع باقی‌ماندن DLLها و فایل‌های قدیمی در `releases/<version>` هنگام انتشار مجدد همان نسخه.
- آماده‌سازی پوشه نسخه‌دار جدید در مسیر مستقل، اعتبارسنجی EXE و جابه‌جایی پوشه‌ای با بازگردانی پوشه قبلی در صورت شکست انتقال.
- حفظ پوشه قبلی و ثبت هشدار اگر پاک‌سازی نسخه قدیمی شکست بخورد.
- بدون تغییر در منطق ساخت و صحت‌سنجی `latest`.

## اعتبارسنجی

Static review: COMPLETED. Automated tests: NOT_RUN. Build validation: NOT_RUN. Runtime review: PENDING_USER. Testing owner: USER.
