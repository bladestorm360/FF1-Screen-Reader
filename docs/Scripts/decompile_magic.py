# Ghidra headless script to decompile FF1 magic/ability functions
# Compatible with Jython 2.7 (Ghidra's Python interpreter)
# Parses il2cpp.h for type information before decompiling
#
# FF1-specific: Classic spell system with spell charges per level.
# This script targets ability data structures and spell mechanics.

from ghidra.app.decompiler import DecompInterface
from ghidra.app.util.cparser.C import CParser
from ghidra.program.model.data import DataTypeConflictHandler
from ghidra.util.task import ConsoleTaskMonitor
from ghidra.program.model.symbol import SourceType
import codecs
import json
import os

# Target functions to decompile (RVA -> name mapping)
# These are Relative Virtual Addresses - image base will be added at runtime
TARGET_FUNCTIONS_RVA = {
    # ============================================================
    # OwnedAbility - Player's owned spell instance (TypeDefIndex: 6909)
    # ============================================================
    0x272320: "OwnedAbility$$get_Ability",
    0x268150: "OwnedAbility$$get_Content",
    0x3B8150: "OwnedAbility$$get_SkillLevel",
    0x3B8220: "OwnedAbility$$set_SkillLevel",
    0x3B8100: "OwnedAbility$$get_MesIdName",
    0x3B80B0: "OwnedAbility$$get_MesIdDescription",
    0x3B7FA0: "OwnedAbility$$ctor",

    # ============================================================
    # Ability - Master spell data (TypeDefIndex: 6697)
    # ============================================================
    0xC79510: "Ability$$get_AbilityLv",
    0xC799A0: "Ability$$get_TypeId",
    0xC799E0: "Ability$$get_AbilityGroupId",
    0xC7A950: "Ability$$get_UseValue",
    0xC7A890: "Ability$$get_StandardValue",
    0xC792A0: "Ability$$get_Id",
    0xC7A120: "Ability$$ctor_masterLine",
    0xC7A310: "Ability$$get_AttributeId",
    0xC7A8D0: "Ability$$get_SystemId",
    0xC7A910: "Ability$$get_UseJobGroupId",

    # ============================================================
    # OwnedAbilitySlotData - Spell slot management (TypeDefIndex: 6910)
    # FF1 has 3 spell slots per level (8 levels)
    # ============================================================
    0x3B7D10: "OwnedAbilitySlotData$$get_Level",
    0x3B7D30: "OwnedAbilitySlotData$$set_Level",
    0x3B74A0: "OwnedAbilitySlotData$$IsAllEmpty",
    0x3B7420: "OwnedAbilitySlotData$$GetAbility",
    0x3B75A0: "OwnedAbilitySlotData$$SetAbility",

    # ============================================================
    # OwnedCharacterClient - Character spell management (TypeDefIndex: 6961)
    # ============================================================
    0x2A4050: "OwnedCharacterClient$$AddAbility",
    0x2ADA80: "OwnedCharacterClient$$RemoveAbility",
    0x2B1350: "OwnedCharacterClient$$SetSlotAbility",
    0x2AF850: "OwnedCharacterClient$$SetAbility",
    0x2AB780: "OwnedCharacterClient$$ForgetAbility",
}

# Paths
OUTPUT_PATH = "D:\\Games\\Dev\\Unity\\FFPR\\ff1\\ff1-screen-reader\\docs\\scripts\\decompiled_magic.c"
SCRIPT_JSON_PATH = "D:\\Games\\Dev\\Unity\\FFPR\\ff1\\script.json"
IL2CPP_HEADER_PATH = "D:\\Games\\Dev\\Unity\\FFPR\\ff1\\il2cpp_ghidra.h"

def parse_il2cpp_header(program):
    """Parse il2cpp_ghidra.h and apply types to the program's data type manager."""
    if not os.path.exists(IL2CPP_HEADER_PATH):
        print("WARNING: il2cpp_ghidra.h not found at: " + IL2CPP_HEADER_PATH)
        return False

    print("Parsing IL2CPP header: " + IL2CPP_HEADER_PATH)
    print("This may take a few minutes for large headers...")

    try:
        # Get the program's data type manager
        dtm = program.getDataTypeManager()

        # Read the header file content
        print("Reading header file...")
        with open(IL2CPP_HEADER_PATH, 'r') as f:
            header_content = f.read()

        print("Header size: " + str(len(header_content)) + " bytes")
        print("Starting C parser...")

        # Create C parser with the program's data type manager
        parser = CParser(dtm)

        # Parse the header content as a string
        try:
            parsed_dtm = parser.parse(header_content)

            if parsed_dtm is not None:
                print("Parsing completed, applying types to program...")
                iterator = dtm.getAllDataTypes()
                count = 0
                while iterator.hasNext():
                    iterator.next()
                    count += 1

                print("Data type manager now has " + str(count) + " types")
                return True
            else:
                print("Parser returned None - types may have been added directly to DTM")
                return True

        except Exception as parse_error:
            error_str = str(parse_error)
            print("C Parser error: " + error_str)

            if "line" in error_str.lower():
                print("This may be a syntax error in the header file.")
                print("Consider editing il2cpp_ghidra.h to fix or comment out the problematic section.")

            # Try alternative method
            print("Attempting alternative parsing method...")
            try:
                from ghidra.app.util.cparser.C import CParserUtils
                from ghidra.app.util import MessageLog
                log = MessageLog()

                file_list = [IL2CPP_HEADER_PATH]
                include_paths = []

                CParserUtils.parseHeaderFiles(dtm, file_list, include_paths, log, ConsoleTaskMonitor())

                if log.hasMessages():
                    print("Parser messages: " + log.toString())

                print("Alternative parsing completed")
                return True
            except Exception as alt_error:
                print("Alternative parsing also failed: " + str(alt_error))
                return False

    except Exception as e:
        print("Error parsing il2cpp_ghidra.h: " + str(e))
        import traceback
        traceback.print_exc()
        return False

def apply_il2cpp_symbols(program):
    """Apply IL2CPP symbol names from script.json."""
    if not os.path.exists(SCRIPT_JSON_PATH):
        print("script.json not found at: " + SCRIPT_JSON_PATH)
        return 0

    print("Loading IL2CPP symbols from: " + SCRIPT_JSON_PATH)
    try:
        with codecs.open(SCRIPT_JSON_PATH, 'r', 'utf-8') as f:
            data = json.load(f)

        symbol_table = program.getSymbolTable()
        address_factory = program.getAddressFactory()
        image_base = program.getImageBase().getOffset()
        applied = 0

        if "ScriptMethod" in data:
            for method in data["ScriptMethod"]:
                addr = method.get("Address")
                name = method.get("Name")
                if addr and name:
                    for target_rva, target_name in TARGET_FUNCTIONS_RVA.items():
                        if name == target_name or name.replace(".", "$$") == target_name:
                            try:
                                ghidra_addr = address_factory.getDefaultAddressSpace().getAddress(image_base + addr)
                                clean_name = name.replace("$$", "__").replace("<", "_").replace(">", "_").replace(",", "_")
                                symbol_table.createLabel(ghidra_addr, clean_name, SourceType.IMPORTED)
                                applied += 1
                            except Exception as e:
                                pass

        print("Applied " + str(applied) + " IL2CPP symbols")
        return applied
    except Exception as e:
        print("Error loading script.json: " + str(e))
        return 0

def decompile_function_at_address(decompiler, program, rva, name):
    """Decompile function at given RVA and return C code."""
    address_factory = program.getAddressFactory()
    image_base = program.getImageBase().getOffset()
    abs_addr = image_base + rva

    try:
        ghidra_addr = address_factory.getDefaultAddressSpace().getAddress(abs_addr)
        func = getFunctionAt(ghidra_addr)

        if func is None:
            print("    Creating function at 0x{:X}...".format(abs_addr))
            func = createFunction(ghidra_addr, name.replace("$$", "_"))
            if func is None:
                return None, "Could not create function at 0x{:X}".format(abs_addr)

        results = decompiler.decompileFunction(func, 120, ConsoleTaskMonitor())

        if results.decompileCompleted():
            decomp_func = results.getDecompiledFunction()
            if decomp_func:
                return decomp_func.getC(), None
            else:
                return None, "Decompilation returned no result"
        else:
            error_msg = results.getErrorMessage()
            if error_msg:
                return None, "Decompilation failed: " + str(error_msg)
            else:
                return None, "Decompilation failed (unknown error)"

    except Exception as e:
        return None, "Exception: " + str(e)

def run():
    """Main script entry point."""
    print("=" * 70)
    print("FF1 Magic/Ability System Decompiler")
    print("=" * 70)

    program = getCurrentProgram()
    if program is None:
        print("ERROR: No program loaded!")
        return

    image_base = program.getImageBase().getOffset()
    print("Program: " + program.getName())
    print("Image Base: 0x{:X}".format(image_base))
    print("Output: " + OUTPUT_PATH)
    print("")

    # Step 1: Parse IL2CPP header for type information
    print("-" * 70)
    print("STEP 1: Parsing IL2CPP type definitions")
    print("-" * 70)
    types_parsed = parse_il2cpp_header(program)
    if types_parsed:
        print("Type parsing completed successfully")
    else:
        print("Type parsing failed or skipped - decompilation will use generic types")
    print("")

    # Step 2: Apply symbol names from script.json
    print("-" * 70)
    print("STEP 2: Applying IL2CPP symbol names")
    print("-" * 70)
    apply_il2cpp_symbols(program)
    print("")

    # Step 3: Decompile target functions
    print("-" * 70)
    print("STEP 3: Decompiling target functions")
    print("-" * 70)
    print("Initializing decompiler...")
    decompiler = DecompInterface()
    decompiler.openProgram(program)

    results = []
    results.append("/*")
    results.append(" * FF1 Decompiled Functions - Magic/Ability System")
    results.append(" * Generated by Ghidra headless analysis")
    results.append(" * Program: " + program.getName())
    results.append(" * Image Base: 0x{:X}".format(image_base))
    results.append(" * IL2CPP types applied: " + str(types_parsed))
    results.append(" *")
    results.append(" * Purpose: Understanding FF1's spell system for screen reader accessibility")
    results.append(" *")
    results.append(" * FF1 Magic System:")
    results.append(" *   - Classic spell charge system (spell uses per rest)")
    results.append(" *   - 8 spell levels, each with limited charges")
    results.append(" *   - 3 spell slots per level per character")
    results.append(" *   - Spells purchased from shops and assigned to slots")
    results.append(" *")
    results.append(" * Key classes:")
    results.append(" *   - OwnedAbility: Player's spell instance with name/description")
    results.append(" *   - Ability: Master data for spell properties (damage, MP cost, etc.)")
    results.append(" *   - OwnedAbilitySlotData: Spell slot management per level")
    results.append(" *   - OwnedCharacterClient: Character spell assignment operations")
    results.append(" */")
    results.append("")

    success_count = 0
    fail_count = 0

    # Group functions by class for better organization
    current_class = ""
    for rva, name in sorted(TARGET_FUNCTIONS_RVA.items(), key=lambda x: x[1]):
        abs_addr = image_base + rva

        # Extract class name for grouping
        class_name = name.split("$$")[0] if "$$" in name else "Unknown"
        if class_name != current_class:
            current_class = class_name
            results.append("")
            results.append("/" + "=" * 68 + "/")
            results.append("/* " + class_name)
            results.append(" " + "=" * 67 + "/")

        print("")
        print("Decompiling: " + name)
        print("  RVA: 0x{:X} -> Absolute: 0x{:X}".format(rva, abs_addr))

        code, error = decompile_function_at_address(decompiler, program, rva, name)

        results.append("")
        results.append("/" + "*" * 68 + "/")
        results.append("/* " + name)
        results.append(" * RVA: 0x{:X}".format(rva))
        results.append(" * Address: 0x{:X}".format(abs_addr))
        results.append(" " + "*" * 67 + "/")
        results.append("")

        if code:
            results.append(code)
            print("  SUCCESS")
            success_count += 1
        else:
            results.append("/* DECOMPILATION FAILED: " + str(error) + " */")
            print("  FAILED: " + str(error))
            fail_count += 1

    # Write output
    print("")
    print("=" * 70)
    try:
        with codecs.open(OUTPUT_PATH, 'w', 'utf-8') as f:
            f.write('\n'.join(results))
        print("Decompilation complete!")
        print("  Success: " + str(success_count))
        print("  Failed:  " + str(fail_count))
        print("  Output:  " + OUTPUT_PATH)
    except Exception as e:
        print("ERROR writing output file: " + str(e))
        try:
            with open(OUTPUT_PATH, 'w') as f:
                f.write('\n'.join(results))
            print("Output written (fallback mode): " + OUTPUT_PATH)
        except Exception as e2:
            print("FALLBACK ALSO FAILED: " + str(e2))

    print("=" * 70)
    decompiler.dispose()

# Run the script
run()
