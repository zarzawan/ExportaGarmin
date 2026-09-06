"""Acumulación de texto solo cuando el formato solicitado lo requiere."""
class TextOutput(list):
    def __init__(self, *, enabled=True):
        super().__init__()
        self.enabled = enabled

    def append(self, value):
        if self.enabled:
            super().append(value)

    def extend(self, values):
        if self.enabled:
            super().extend(values)
