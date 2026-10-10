#pragma once
#include <windows.h>
#include <stdlib.h>
#include <string.h>
#ifndef S_ISDIR
#define S_ISDIR(m) (((m) & _S_IFMT) == _S_IFDIR)
#endif
struct dirent { char d_name[MAX_PATH]; };
typedef struct { HANDLE handle; WIN32_FIND_DATAA data; int first; struct dirent entry; } DIR;
static DIR* opendir(const char* path) {
    char pattern[4096];
    if (strlen(path) + 3 >= sizeof(pattern)) return NULL;
    strcpy(pattern, path); strcat(pattern, "/*");
    DIR* dir = (DIR*)calloc(1, sizeof(DIR));
    if (!dir) return NULL;
    dir->handle = FindFirstFileA(pattern, &dir->data); dir->first = 1;
    if (dir->handle == INVALID_HANDLE_VALUE) { free(dir); return NULL; }
    return dir;
}
static struct dirent* readdir(DIR* dir) {
    if (!dir || (!dir->first && !FindNextFileA(dir->handle, &dir->data))) return NULL;
    dir->first = 0; strcpy(dir->entry.d_name, dir->data.cFileName); return &dir->entry;
}
static int closedir(DIR* dir) { if (!dir) return -1; FindClose(dir->handle); free(dir); return 0; }
