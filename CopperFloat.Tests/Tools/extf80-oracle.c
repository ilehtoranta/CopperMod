/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 *
 * Build this adapter against an external Berkeley SoftFloat 3e build.  It is
 * test tooling only; CopperFloat does not compile or distribute SoftFloat.
 */

#include <inttypes.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "softfloat.h"

static extFloat80_t parse_ext(const char *sign_exp, const char *significand)
{
    extFloat80_t value;
    value.signExp = (uint16_t)strtoul(sign_exp, NULL, 16);
    value.signif = (uint64_t)strtoull(significand, NULL, 16);
    return value;
}

static void print_ext(extFloat80_t value)
{
    printf("%04" PRIX16 "\t%016" PRIX64 "\t%02" PRIXFAST8 "\n",
        value.signExp,
        value.signif,
        softfloat_exceptionFlags);
}

static void print_integer(uint64_t value)
{
    printf("0000\t%016" PRIX64 "\t%02" PRIXFAST8 "\n",
        value,
        softfloat_exceptionFlags);
}

static int execute(const char *operation, extFloat80_t a, extFloat80_t b)
{
    extFloat80_t result;

    softfloat_exceptionFlags = 0;
    if (!strcmp(operation, "add")) result = extF80_add(a, b);
    else if (!strcmp(operation, "sub")) result = extF80_sub(a, b);
    else if (!strcmp(operation, "mul")) result = extF80_mul(a, b);
    else if (!strcmp(operation, "div")) result = extF80_div(a, b);
    else if (!strcmp(operation, "sqrt")) result = extF80_sqrt(a);
    else if (!strcmp(operation, "round")) result = extF80_roundToInt(a, softfloat_roundingMode, true);
    else if (!strcmp(operation, "to_i32")) {
        print_integer((uint32_t)extF80_to_i32(a, softfloat_roundingMode, true));
        return 0;
    }
    else if (!strcmp(operation, "to_i64")) {
        print_integer((uint64_t)extF80_to_i64(a, softfloat_roundingMode, true));
        return 0;
    }
    else if (!strcmp(operation, "to_f32")) {
        print_integer(extF80_to_f32(a).v);
        return 0;
    }
    else if (!strcmp(operation, "to_f64")) {
        print_integer(extF80_to_f64(a).v);
        return 0;
    }
    else {
        fprintf(stderr, "unsupported operation: %s\n", operation);
        return 2;
    }

    print_ext(result);
    return 0;
}

int main(int argc, char **argv)
{
    extFloat80_t a;
    extFloat80_t b;
    unsigned int sign_exp_a;
    unsigned int sign_exp_b;
    uint64_t significand_a;
    uint64_t significand_b;

    if (argc == 6 && !strcmp(argv[1], "batch")) {
        softfloat_roundingMode = (uint_fast8_t)strtoul(argv[3], NULL, 0);
        extF80_roundingPrecision = (uint_fast8_t)strtoul(argv[4], NULL, 0);
        softfloat_detectTininess = (uint_fast8_t)strtoul(argv[5], NULL, 0);
        while (scanf("%x %" SCNx64 " %x %" SCNx64,
            &sign_exp_a,
            &significand_a,
            &sign_exp_b,
            &significand_b) == 4) {
            a.signExp = (uint16_t)sign_exp_a;
            a.signif = significand_a;
            b.signExp = (uint16_t)sign_exp_b;
            b.signif = significand_b;
            printf("%04X\t%016" PRIX64 "\t%04X\t%016" PRIX64 "\t",
                sign_exp_a,
                significand_a,
                sign_exp_b,
                significand_b);
            if (execute(argv[2], a, b)) return 2;
        }
        return 0;
    }

    if (argc < 8) {
        fprintf(stderr,
            "usage: extf80-oracle op rounding precision tininess aSE aSig bSE bSig\n");
        return 2;
    }

    softfloat_roundingMode = (uint_fast8_t)strtoul(argv[2], NULL, 0);
    extF80_roundingPrecision = (uint_fast8_t)strtoul(argv[3], NULL, 0);
    softfloat_detectTininess = (uint_fast8_t)strtoul(argv[4], NULL, 0);
    a = parse_ext(argv[5], argv[6]);
    b = argc >= 9 ? parse_ext(argv[7], argv[8]) : a;
    return execute(argv[1], a, b);
}
