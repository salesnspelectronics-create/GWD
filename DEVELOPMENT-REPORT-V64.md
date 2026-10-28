# V64 Development Report

Observed V63 runtime inconsistency: the SQL Server page showed `Verified — Windows login works` while the global footer remained `SQL —`. The root cause was separate state publication: wizard verification set a local boolean but did not publish the successful result into BackgroundHealthMonitor.Latest.

V64 introduces shared SQL health publication and explicit footer indicators. It also removes a duplicate event subscription added on every Start Over.
