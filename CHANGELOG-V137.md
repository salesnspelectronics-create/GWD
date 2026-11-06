# V137-R — مدیریت خطای آرگومان نامعتبر در CLI

- `Arguments.Parse(args)` به بلوک `try` اصلی منتقل شد.
- خطای `CliFailure` هنگام Parse اکنون از همان مسیر خروجی ساختاریافته `CliResult.Fail` عبور می‌کند.
- قبل از ساخته‌شدن شیء `Arguments`، نام دستور با fallback از اولین آرگومان انتخاب می‌شود.
- دستورات معتبر و منطق Deployment بدون تغییر باقی ماندند.
- Static source review: COMPLETED؛ Automated Tests و Build: NOT_RUN؛ Runtime: PENDING_USER.
