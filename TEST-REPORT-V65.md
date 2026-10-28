# TEST REPORT — V65

## Static verification
PASS — version advanced to 65.0.0.
PASS — target app pool is stopped and polled before hosted-folder replacement.
PASS — hosted-folder deletion uses bounded retry/backoff for IO/access locks.
PASS — validated rollback backup remains outside the hosted folder and is not deleted on lock failure.

## Runtime acceptance
PENDING — requires Windows/IIS runtime retest against the NSP Office Prepared IIS Publish scenario that failed in V64.

Expected evidence: APPPOOL_STOP_CONFIRMED=PASS, HOSTED_FOLDER_RELEASE=PASS, successful activation, IIS/app-pool restart, and HTTP health PASS.
