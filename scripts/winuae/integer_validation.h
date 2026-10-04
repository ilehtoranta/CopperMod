/* Test-only Basic-preset validation. Record layout is pinned to
 * emoon/m68k_cpu_tester_api 025b999, gencpu/cputest.cpp save_exception.
 * No trace, group-2/fault combination or undocumented restart image is skipped.
 * An unsupported record fails the audit instead of claiming frame coverage. */
static uae_u16 copper_defined_sr = 0xffff;
static unsigned int copper_frame_checks, copper_masked_cases;

void M68KTester_set_defined_sr(uae_u16 mask) {
    copper_defined_sr = mask;
    if (mask != 0xffff) copper_masked_cases++;
}
unsigned int M68KTester_frame_checks(void) { return copper_frame_checks; }
unsigned int M68KTester_masked_cases(void) { return copper_masked_cases; }

static uae_u8* validate_exception(struct registers* actual, uae_u8* p,
    int excnum, int sameexc, int* experr) {
    unsigned int length = *p++;
    uae_u8* next = p;
    uae_u8* fields;
    int frame_length = 0;
    uae_u8 expected[16] = {0};
    if (length == 0) goto unsupported;
    if (length == 0xff) {
        if (last_exception_len <= 0) goto unsupported;
        fields = last_exception;
        length = last_exception_len;
    } else {
        next = p + length;
        memcpy(last_exception, p, length);
        last_exception_len = length;
        fields = last_exception;
    }
    uae_u8* end = fields + length;
    /* First byte describes extra trace/group-2 exceptions. Basic has none. */
    if (*fields++ != 0 || excnum == 2 || excnum == 3)
        goto unsupported;
    expected[0] = last_registers.sr >> 8;
    expected[1] = last_registers.sr;
    pl(expected + 2, last_registers.pc);
    if (cpu_lvl == 0) {
        frame_length = 6;
    } else {
        if (end - fields < 2) goto unsupported;
        unsigned int format = (fields[0] << 8) | fields[1];
        expected[6] = *fields++;
        expected[7] = *fields++;
        switch (format >> 12) {
            case 0: frame_length = 8; break;
            case 2:
            case 3:
            case 4: {
                uae_u32 value = opcode_memory_addr;
                if (fields >= end) goto unsupported;
                fields = restore_rel_ordered(fields, &value);
                pl(expected + 8, value);
                frame_length = 12;
                if ((format >> 12) == 4) {
                    value = opcode_memory_addr;
                    if (fields >= end) goto unsupported;
                    fields = restore_rel_ordered(fields, &value);
                    pl(expected + 12, value);
                    frame_length = 16;
                }
                break;
            }
            default: goto unsupported;
        }
    }
    if (fields != end) goto unsupported;
    if (!sameexc) return next; /* The caller independently fails vector mismatch. */
    uae_u8* observed = translate_to_native(actual->excframe);
    if (!observed) goto unsupported;
    copper_frame_checks++;
    uae_u16 sr_mask = sr_undefined_mask & test_ccrignoremask & copper_defined_sr;
    for (int index = 0; index < frame_length; index++) {
        uae_u8 mask = index == 0 ? sr_mask >> 8 : index == 1 ? sr_mask : 0xff;
        if ((expected[index] & mask) == (observed[index] & mask)) continue;
        addinfo();
        sprintf(outbp, "Exception %d frame byte %d: expected %02x but got %02x (mask %02x)\n",
            excnum, index, expected[index], observed[index], mask);
        outbp += strlen(outbp);
        errors++;
        *experr = 1;
        break;
    }
    return next;
unsupported:
    addinfo();
    sprintf(outbp, "Unsupported or malformed Basic exception record (vector %d)\n", excnum);
    outbp += strlen(outbp);
    errors++;
    *experr = 1;
    return next;
}
