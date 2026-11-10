# Verificación funcional

Este ejecutable de consola devuelve `0` si todas las pruebas pasan y `1` si alguna falla. No necesita un dispositivo serie ni mueve hardware.

En Visual Studio, compila la solución y ejecuta `ArmStudio.Tests`. También puedes ejecutar `ArmStudio.Tests\bin\Release\ArmStudio.Tests.exe` tras compilar en Release.

Comprueba el subconjunto de instrucciones, los diagnósticos por línea, la interpolación lineal y circular, las restricciones numéricas, el guardado de proyectos y los cambios de estado del simulador. Las pruebas de cancelación comprueban que detener un movimiento también exige volver a habilitar los motores. Se ejecutan con cultura española para detectar conversiones numéricas dependientes de la configuración regional.

También incluye 24 comprobaciones de articulaciones: límites J1–J6, aislamiento entre ejes, interpolación, cancelación, rechazo de concurrencia y separación explícita de la práctica cartesiana. Total: 53 comprobaciones del núcleo.

La conexión física requiere una comprobación independiente en Windows con la placa y el firmware elegidos. Estas pruebas no validan cinemática, electrónica ni seguridad física.
