# Berkeley TestFloat oracle adapter

`extf80-oracle.c` is a test-only adapter for an external Berkeley SoftFloat
3e build. CopperFloat neither compiles nor distributes SoftFloat code.

Example Linux build, from a SoftFloat `Linux-x86_64-GCC` build directory:

```sh
gcc -O2 -DSOFTFLOAT_FAST_INT64 -DLITTLEENDIAN \
  -I../../source/include \
  /path/to/CopperFloat.Tests/Tools/extf80-oracle.c \
  softfloat.a \
  -o extf80-oracle
```

Set `COPPERFLOAT_TESTFLOAT_ORACLE` to the resulting executable to enable the
external level-2 oracle test. The committed level-1 fixture does not require
the executable.
