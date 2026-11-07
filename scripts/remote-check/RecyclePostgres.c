// Task-only PostgreSQL harness: preserve cleanup targets inside an owned root.
// Inherited seccomp still rejects every actual unlink/unlinkat/rmdir syscall.
#define _GNU_SOURCE
#include <dirent.h>
#include <errno.h>
#include <fcntl.h>
#include <limits.h>
#include <stdatomic.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <sys/syscall.h>
#include <time.h>
#include <unistd.h>

static _Atomic unsigned long serial;

static int preserve(int dirfd, const char *name, int directory) {
    char input[PATH_MAX], source[PATH_MAX], destination[PATH_MAX];
    const char *root = getenv("QA07_PG_ROOT"), *recycle = getenv("QA07_PG_RECYCLE");
    if (!root || !recycle || !name || !*name) { errno = EACCES; return -1; }
    int length;
    if (*name == '/' || dirfd == AT_FDCWD) length = snprintf(input, sizeof input, "%s", name);
    else length = snprintf(input, sizeof input, "/proc/self/fd/%d/%s", dirfd, name);
    if (length < 0 || (size_t)length >= sizeof input) { errno = ENAMETOOLONG; return -1; }
    struct stat info;
    if (lstat(input, &info)) return -1;
    if (S_ISLNK(info.st_mode) || (S_ISREG(info.st_mode) && info.st_nlink != 1)) { errno = EACCES; return -1; }
    if (!realpath(input, source)) return -1;
    size_t n = strlen(root);
    if (strncmp(source, root, n) || source[n] != '/' ||
        !strncmp(source, recycle, strlen(recycle))) { errno = EACCES; return -1; }
    if (directory) {
        if (!S_ISDIR(info.st_mode)) { errno = ENOTDIR; return -1; }
        DIR *stream = opendir(source);
        if (!stream) return -1;
        struct dirent *entry;
        errno = 0;
        while ((entry = readdir(stream))) {
            if (strcmp(entry->d_name, ".") && strcmp(entry->d_name, "..")) {
                closedir(stream); errno = ENOTEMPTY; return -1;
            }
        }
        int read_error = errno;
        if (closedir(stream) && !read_error) read_error = errno;
        if (read_error) { errno = read_error; return -1; }
    } else if (S_ISDIR(info.st_mode)) { errno = EISDIR; return -1; }
    // The fixture owns all ancestors and does not allow concurrent outside writers.
    char checked[PATH_MAX];
    if (lstat(recycle, &info) || !S_ISDIR(info.st_mode) || !realpath(recycle, checked) || strcmp(checked, recycle)) {
        errno = EACCES; return -1;
    }
    struct timespec now;
    if (clock_gettime(CLOCK_MONOTONIC, &now)) return -1;
    length = snprintf(destination, sizeof destination, "%s/%ld-%ld-%ld-%lu", recycle,
        (long)getpid(), now.tv_sec, now.tv_nsec, atomic_fetch_add(&serial, 1));
    if (length < 0 || (size_t)length >= sizeof destination) { errno = ENAMETOOLONG; return -1; }
    // RENAME_NOREPLACE cannot overwrite a previously retained object.
    return (int)syscall(SYS_renameat2, AT_FDCWD, source, AT_FDCWD, destination, 1);
}

int unlink(const char *path) { return preserve(AT_FDCWD, path, 0); }
int unlinkat(int fd, const char *path, int flags) {
    if (flags & ~AT_REMOVEDIR) { errno = EINVAL; return -1; }
    return preserve(fd, path, !!(flags & AT_REMOVEDIR));
}
int rmdir(const char *path) { return preserve(AT_FDCWD, path, 1); }
int remove(const char *path) {
    struct stat info;
    if (lstat(path, &info)) return -1;
    return preserve(AT_FDCWD, path, S_ISDIR(info.st_mode));
}
