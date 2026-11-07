# Remote PostgreSQL long workspace paths

The combined142751-164a6e5f442b4326845ca6c2c49d6dbe run failed before product-browser startup because its Unix-domain socket path under /data exceeded PostgreSQL's107-byte pathname maximum. The runner already connects every SQL client through127.0.0.1 TCP. It now passes `-k ''` to disable unused Unix sockets while preserving `-h 127.0.0.1`, the task port, mmap and retained cleanup behavior.

Final isolated environment run143310-11f6257041aa462584d7988a298c8989, source archive1d496539adf3997f2611f982399700f4f67170325ce6f145b205a08d8bbb9fd4, exits0 on the same long /data root. Both server starts listen on127.0.0.1, stop cleanly, and the committed row survives restart; shutdown/remaining-native lists are empty,24 recycle entries retained. Economic read-only code/evidence review PASS.

This fixes the runner environment prerequisite. It does not convert the earlier failed combined browser run into a pass; that run remains failed until separately repeated.
