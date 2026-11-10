# Del mando al brazo impreso en 3D

ArmStudio ya permite practicar las instrucciones y probar la conexión. El siguiente trabajo depende de la mecánica elegida; no hay un modelo físico definido todavía.

## Datos que faltan

| Decisión | Por qué importa |
| --- | --- |
| Número de ejes y tipo de pinza | Determina los canales de control y qué poses son posibles |
| Longitud de cada eslabón y posición de los ejes | Necesarias para la cinemática directa e inversa |
| Servos o motores paso a paso y sus drivers | Cambia el cableado, la resolución y el firmware |
| Placa, memoria y frecuencia | Determina si puede calcular trayectorias y controlar todos los ejes |
| Alimentación y corriente por motor | Dimensiona fuentes, protecciones y conectores |
| Referencias y límites mecánicos | Define homing, calibración y rango de cada articulación |
| Finales de carrera, encoders y parada física | Permite conocer y limitar el estado del mecanismo |

Un Arduino Uno podría servir para algunas prácticas, pero no se ha seleccionado aún como controlador final. Un brazo de varios ejes puede necesitar una placa con más capacidad y drivers externos. El USB lleva comunicación; la alimentación de motores se dimensiona aparte.

## Orden de integración

1. Verificar USB con el sketch de diagnóstico actual, sin conectar motores.
2. Probar un único eje con su alimentación, driver y límites.
3. Definir cero y rango de cada articulación y almacenar su calibración.
4. Implementar cinemática con las dimensiones reales; rechazar destinos no alcanzables.
5. Incorporar trayectorias, límites de velocidad y aceleración, vigilancia de conexión y confirmación de fin de movimiento.
6. Añadir un transporte de movimiento a C# y acordar un protocolo versionado. El protocolo diagnóstico actual no declara capacidades de movimiento.
7. Validar `MoveL` y `MoveC` a baja velocidad con una zona de trabajo delimitada.

La ejecución física deberá esperar confirmación del controlador para cada instrucción; recibir texto en un puerto no prueba que el motor haya llegado. Una desconexión o error no debe causar la repetición automática del último movimiento. La parada cableada y la pérdida de comunicación deben resolverse en el controlador físico.

## Para el portafolio

La entrega actual se puede presentar como una primera fase: mando de escritorio, lenguaje de trayectorias, geometría circular, simulación numérica y protocolo USB verificable. Cuando exista hardware, documenta con fotografías, esquema eléctrico, dimensiones, vídeo y pruebas de precisión el paso del comando al movimiento real.
