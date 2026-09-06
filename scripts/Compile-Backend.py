"""Compila el backend portable y verifica todos sus nombres fuente."""
import argparse
import marshal
import py_compile
import types
from pathlib import Path


def validate_code(code, source_name):
    if code.co_filename != source_name:
        raise ValueError("El bytecode contiene un nombre fuente inesperado.")
    for constant in code.co_consts:
        if isinstance(constant, types.CodeType):
            validate_code(constant, source_name)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    modules = (root / "backend-modules.txt").read_text(encoding="utf-8").splitlines()
    args.output.mkdir(parents=True, exist_ok=True)
    for module in modules:
        if Path(module).name != module or not module.endswith(".py"):
            raise ValueError("El manifiesto del backend contiene una ruta no admitida.")
        target = args.output / Path(module).with_suffix(".pyc")
        py_compile.compile(str(root / module), cfile=str(target), dfile=module,
                           doraise=True, invalidation_mode=py_compile.PycInvalidationMode.CHECKED_HASH)
        validate_code(marshal.loads(target.read_bytes()[16:]), module)
    print(f"{len(modules)} módulos compilados sin rutas del equipo de construcción.")


if __name__ == "__main__":
    main()
