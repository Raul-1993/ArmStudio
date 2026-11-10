! ARM STUDIO · Práctica de trayectoria
! Posiciones simuladas en mm y grados.
MODULE Practice
    PROC main()
        MoveL pHome, v50, fine, tool0;
        MoveL p1, v50, fine, tool0;
        MoveC pVia, p2, v50, z0, tool0;
        MoveL p3, v50, fine, tool0;
        WaitTime 0.5;
        MoveL pHome, v50, fine, tool0;
    ENDPROC
ENDMODULE
