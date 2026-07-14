#!/usr/bin/env bash
# Copyright (C) 2026 Ilkka Lehtoranta
# SPDX-License-Identifier: MIT

set -eu

generator=${TESTFLOAT_GEN:?Set TESTFLOAT_GEN to Berkeley TestFloat 3e testfloat_gen}
output=${1:-../Fixtures/testfloat-extf80-level1.tsv}
conversion_output=${2:-../Fixtures/testfloat-extf80-conversions-level1.tsv}
sample_count=${COPPERFLOAT_TESTFLOAT_SAMPLE_COUNT:-8}

printf 'operation\trounding\tprecision\ttininess\taSignExponent\taSignificand\tbSignExponent\tbSignificand\tresultSignExponent\tresultSignificand\tflags\n' > "$output"

for rounding in near_even minMag min max; do
    case "$rounding" in
        near_even) rounding_option=-rnear_even ;;
        minMag) rounding_option=-rminMag ;;
        min) rounding_option=-rmin ;;
        max) rounding_option=-rmax ;;
    esac

    for precision in 24 53 64; do
        case "$precision" in
            24) precision_option=-precision32 ;;
            53) precision_option=-precision64 ;;
            64) precision_option=-precision80 ;;
        esac

        for tininess in before after; do
            if [ "$tininess" = before ]; then
                tininess_option=-tininessbefore
            else
                tininess_option=-tininessafter
            fi

            for operation in add sub mul div; do
                "$generator" -level 1 "$rounding_option" "$precision_option" "$tininess_option" "extF80_$operation" |
                    head -n "$sample_count" |
                    awk -v op="$operation" -v round="$rounding" -v precision="$precision" -v tiny="$tininess" \
                        '{ printf "%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n", op, round, precision, tiny, substr($1,1,4), substr($1,5,16), substr($2,1,4), substr($2,5,16), substr($3,1,4), substr($3,5,16), $4 }' \
                        >> "$output"
            done

            for operation in sqrt roundToInt; do
                exact_option=
                if [ "$operation" = roundToInt ]; then
                    exact_option=-exact
                fi
                "$generator" -level 1 "$rounding_option" "$precision_option" "$tininess_option" $exact_option "extF80_$operation" |
                    head -n "$sample_count" |
                    awk -v op="$operation" -v round="$rounding" -v precision="$precision" -v tiny="$tininess" \
                        '{ printf "%s\t%s\t%s\t%s\t%s\t%s\t0000\t0000000000000000\t%s\t%s\t%s\n", op, round, precision, tiny, substr($1,1,4), substr($1,5,16), substr($2,1,4), substr($2,5,16), $3 }' \
                        >> "$output"
            done
        done
    done
done

printf 'operation\trounding\ttininess\taSignExponent\taSignificand\tresult\tflags\n' > "$conversion_output"
for rounding in near_even minMag min max; do
    case "$rounding" in
        near_even) rounding_option=-rnear_even ;;
        minMag) rounding_option=-rminMag ;;
        min) rounding_option=-rmin ;;
        max) rounding_option=-rmax ;;
    esac

    for tininess in before after; do
        if [ "$tininess" = before ]; then
            tininess_option=-tininessbefore
        else
            tininess_option=-tininessafter
        fi

        for conversion in to_i32 to_i64 to_f32 to_f64; do
            exact_option=
            if [ "$conversion" = to_i32 ] || [ "$conversion" = to_i64 ]; then
                exact_option=-exact
            fi
            "$generator" -level 1 -n 912 "$rounding_option" "$tininess_option" $exact_option "extF80_$conversion" |
                head -n "$sample_count" |
                awk -v op="$conversion" -v round="$rounding" -v tiny="$tininess" \
                    '{ printf "%s\t%s\t%s\t%s\t%s\t%s\t%s\n", op, round, tiny, substr($1,1,4), substr($1,5,16), $2, $3 }' \
                    >> "$conversion_output"
        done
    done
done
