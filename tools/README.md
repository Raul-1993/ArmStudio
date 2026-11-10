# Verificación adicional de transportes

## Formulario nativo

`UiSmoke.cs` realiza 33 comprobaciones del formulario real: conexión, habilitación, paso, pausa, ejecución completa, control manual, articulaciones J1–J6, límites, parada y cierre durante movimiento. Se compila tras construir la aplicación, con referencias a `ArmStudio.exe`, `ArmStudio.Core.dll`, `System.Drawing` y `System.Windows.Forms`. Ejecútalo en un escritorio Windows o con Mono y un servidor X en Linux. `run-ui-headless.py` inicia Xvfb para la segunda opción; necesita `Xvfb` en PATH o la variable `ARMSTUDIO_XVFB`.

La captura se guarda en `preview.png` del directorio actual, o en la ruta de `ARMSTUDIO_PREVIEW` si se define. El comprobador usa reflexión sobre campos privados para verificar estados del formulario y debe revisarse si se renombran estos campos.

## Protocolo serie y sketch

Estas comprobaciones son complementarias al ejecutable `ArmStudio.Tests`. Se ejecutan en Linux con Python 3, Mono (runtime y compilador C# con referencias .NET Framework 4.8) y un compilador C++ como `g++`. No requieren placa, puerto físico ni permisos de administrador.

Desde la raíz del proyecto:

```bash
python3 tools/verify-transports.py
```

Si Mono no está en `PATH` o su compilador está en una ubicación distinta:

```bash
python3 tools/verify-transports.py --mono /ruta/mono --mcs-exe /ruta/mcs.exe
```

El script crea los ejecutables en un directorio temporal y lo elimina al terminar. No modifica el proyecto ni genera binarios en esta carpeta.

Comprueba cinco escenarios con `System.IO.Ports.SerialPort` real y un microcontrolador emulado mediante pseudoterminales de Linux:

1. Saludo correcto, `PING`, `STATUS`, rechazo de movimiento y desconexión.
2. Cancelación durante la conexión.
3. Tiempo máximo de respuesta ante firmware silencioso.
4. Rechazo de una versión de firmware distinta.
5. Rechazo de una respuesta con identificador de petición equivocado y cierre de sesión tras el tiempo máximo.

También compila el archivo `.ino` como C++ usando `fixtures/Arduino.h`, una implementación mínima de `Serial` solo para pruebas. Sus nueve comprobaciones incluyen tramas normales, comandos no admitidos, identificadores incorrectos, líneas demasiado largas y recepción fragmentada. **Esto verifica sintaxis y protocolo del sketch en el ordenador; no sustituye compilar con el core Arduino de la placa real ni cargarlo y probarlo físicamente.**
