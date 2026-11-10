# Conexión USB / serie · protocolo ArmStudio 1

La aplicación implementa conexión, reconocimiento de firmware, diagnóstico y registro de tramas mediante `System.IO.Ports.SerialPort`. El firmware incluido permite probar el enlace con una placa Arduino antes de montar el brazo. **Esta versión del firmware no mueve motores.** La aplicación mantiene `CanMove = false` en el transporte serie aunque la conexión sea correcta.

## Probar la conexión

1. Abre `firmware/ArmStudioDiagnostic/ArmStudioDiagnostic.ino` en Arduino IDE. Selecciona la placa real (por ejemplo, Uno o Nano) y su puerto, y carga el sketch.
2. Cierra el monitor serie de Arduino IDE: un puerto no puede estar abierto por dos programas a la vez.
3. En ArmStudio selecciona USB / serie, actualiza los puertos y elige el COM correspondiente. Mantén 115200 baudios.
4. Pulsa **Conectar**. La aplicación abre el puerto y espera 1,5 segundos por el posible reinicio de la placa. Solo muestra conexión verificada tras recibir el saludo exacto.
5. Pulsa **Probar USB** para comprobar el enlace y consultar el estado. Las tramas TX y RX aparecen en el registro.

Si usas otro baud rate en C#, también debes modificar `Serial.begin(...)` del sketch. El valor inicial en ambos es 115200.

## Formato

- ASCII imprimible, una trama por línea terminada en LF (`\n`). Se acepta CRLF al recibir.
- 115200 baudios, 8 bits, sin paridad, 1 bit de parada; sin control de flujo.
- DTR y RTS se mantienen desactivados; algunos adaptadores reinician igualmente la placa al abrir el puerto.
- El sketch acepta hasta 95 caracteres por trama. El receptor C# rechaza líneas de más de 255 caracteres y datos no ASCII.
- No existe cola de comandos: solo se permite una petición pendiente. Las respuestas de diagnóstico deben tener el mismo identificador que su petición.
- Tiempo máximo de apertura supervisada: 3 s. Tiempo máximo de respuesta: 2 s. Un fallo, cancelación o respuesta incorrecta invalida la sesión; hay que conectar de nuevo.

| PC → placa | Placa → PC | Uso |
| --- | --- | --- |
| `HELLO 1` | `ARMSTUDIO 1 DIAGNOSTIC` | Reconocimiento obligatorio de versión |
| `N1 PING` | `OK N1 PONG` | Prueba del enlace |
| `N2 STATUS` | `OK N2 DIAGNOSTIC` | Identifica el modo del firmware |
| `N3 MOVE_L` | `ERR N3 UNSUPPORTED` | Movimiento rechazado |

Los identificadores crecen durante la sesión. Una línea sin identificador válido recibe `ERR N0 BAD_SEQUENCE`; una trama inválida o demasiado larga recibe `ERR N0 FRAME_TOO_LONG_OR_INVALID`. Una respuesta recibida con otro identificador se registra pero no completa la petición activa.

## Simulador de la aplicación

El transporte de simulación usa comandos internos distintos; no se envían a la placa:

```text
ENABLE 1
MOVE_L 120 0 200 0 0 0 50
MOVE_C 150 30 200 0 0 0 120 60 200 0 0 0 50
WAIT 0.5
ENABLE 0
RESET
RESET_JOINTS
JOG_J 1 5 30
RESET_CARTESIAN
```

`MOVE_L` recibe destino XYZABC y velocidad; `MOVE_C` recibe punto de paso XYZABC, destino XYZABC y velocidad. Los números usan punto decimal. Las traslaciones se expresan en mm y la velocidad en mm/s. ABC son ángulos didácticos interpolados, no una convención de orientación de un robot concreto. Los límites de práctica son XYZ ±500 mm y ABC ±180°. No describen el alcance del futuro brazo. El método `StopAsync()` cancela la operación activa y deshabilita el simulador; `RESET` conserva la posición y deja los motores deshabilitados.

`RESET_JOINTS` inicia una práctica numérica independiente con los seis ejes a cero y el TCP desconocido. `JOG_J` recibe eje (1–6), incremento en grados y velocidad en grados/s; necesita movimientos habilitados y estado articular conocido. Sus límites siguen el perfil de la captura de RobotStudio (J6 admite ±400°, sin normalizar a ±180°). `RESET_CARTESIAN` restaura la práctica XYZ a Z=200 y deja los ángulos articulares desconocidos. Ninguna de estas órdenes se envía al firmware de diagnóstico ni calcula cinemática o homing reales.

## Incorporar el brazo físico después

Será necesario elegir placa, motores, drivers, alimentación, transmisión, geometría de eslabones, finales de carrera y procedimiento de referencia. Entonces se añadirá firmware de movimiento con cinemática, aceleraciones, límites reales, estado/posición medidos, reconocimiento de órdenes y vigilancia de comunicación. La aplicación deberá negociar esa capacidad y comprobar el estado antes de permitir movimiento. El saludo diagnóstico actual nunca debe habilitarlo por sí solo.

El botón STOP de la interfaz es una orden de software. Para el brazo físico se necesita una parada independiente que corte o inhiba la potencia de movimiento, además de límites y supervisión en el controlador. La confirmación visual sirve para observar el resultado, pero no sustituye esas funciones del hardware.
