# Test-independent inventory from the MOVE encoding and full-EA tables.
function Get-Copper68k040OperandWriteKeys([string]$Matrix) {
    if ($Matrix -cnotin @('opcodes','boundaries','indexed')) { throw 'Unknown operand-write matrix' }
    $keys = [Collections.Generic.Dictionary[string,int]]::new([StringComparer]::Ordinal)
    function Operand-Name([int]$Mode,[int]$Register) {
        switch ($Mode) {
            0 { "D$Register" } 1 { "A$Register" } 2 { "(A$Register)" } 3 { "(A$Register)+" }
            4 { "-(A$Register)" } 5 { "d16(A$Register)" } 6 { "index(A$Register)" }
            7 { @('abs.w','abs.l','d16(PC)','index(PC)','immediate')[$Register] }
        }
    }
    function Add-Key([string]$Scenario,[int]$Width,[int]$SourceMode,[int]$SourceRegister,
        [int]$DestinationMode,[int]$DestinationRegister,[string]$Bank,[string]$Trace,[int]$Weight) {
        $source = Operand-Name $SourceMode $SourceRegister
        $destination = Operand-Name $DestinationMode $DestinationRegister
        $key = "68040/MOVE/write-fault/$Scenario/size=$Width/source=$source/destination=$destination/bank=$Bank/T=$Trace"
        if (-not $keys.TryAdd($key,$Weight)) { throw "Duplicate operand-write combination: $key" }
    }
    if ($Matrix -ceq 'opcodes') {
        for ($opcode=0x1000; $opcode -lt 0x4000; $opcode++) {
            $top=$opcode -shr 12; $sm=($opcode -shr 3) -band 7; $sr=$opcode -band 7
            $dm=($opcode -shr 6) -band 7; $dr=($opcode -shr 9) -band 7
            if ($dm -lt 2 -or ($top -eq 1 -and $sm -eq 1) -or
                ($sm -eq 7 -and $sr -gt 4) -or ($dm -eq 7 -and $dr -gt 1)) { continue }
            $width=if($top -eq 1){1}elseif($top -eq 3){2}else{4}
            Add-Key 'opcode' $width $sm $sr $dm $dr 'ISP' '0000' 1
        }
    } elseif ($Matrix -ceq 'boundaries') {
        foreach($width in @(1,2,4)){foreach($bank in @('user','user-M','ISP','MSP')){
        foreach($lane in 0..3){foreach($pair in 0..5){
            Add-Key "boundary/lane=$lane/pair=$pair" $width 0 0 3 7 $bank '0000' 32
        }}}}
    } else {
        foreach($bs in @('False','True')){foreach($is in @('False','True')){foreach($bd in 1..3){
        foreach($iis in @(0,1,2,3,5,6,7)){
            if($is -ceq 'True' -and $iis -ge 5){continue}
            $spec="full/bs=$bs/is=$is/bd=$bd/iis=$iis"
            foreach($width in @(1,2,4)){foreach($register in @(0,7)){foreach($bank in @('user','user-M','ISP','MSP')){
            foreach($trace in @('0000','8000','4000')){foreach($form in @('source','destination','dual')){
                $sm=if($form -ceq 'destination'){3}else{6};$dm=if($form -ceq 'source'){3}else{6}
                Add-Key "indexed/$spec/reg=$register/form=$form" $width $sm $register $dm $register $bank $trace 1
            }}}}}
        }}}}
    }
    $required=if($Matrix -ceq 'opcodes'){7350}elseif($Matrix -ceq 'boundaries'){288}else{14256}
    if($keys.Count -ne $required){throw 'Operand-write reference inventory changed'}
    return ,$keys
}
