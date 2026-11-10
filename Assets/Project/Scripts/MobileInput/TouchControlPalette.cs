using UnityEngine;

namespace PROCEDURAL_GAME.MobileInput
{
    /// <summary>
    /// Cores dos controles de toque: tons de preto e cinza, translúcidos, para não competir com a neblina e a cena.
    /// Aplicadas em runtime (botões e joystick) e também pelo menu Create Mobile Controls, para a cena salva já sair igual.
    /// </summary>
    public static class TouchControlPalette
    {
        // Botão em repouso: disco quase preto, aro cinza, texto cinza claro.
        public static readonly Color ButtonFill = new Color(0.05f, 0.05f, 0.05f, 0.55f);
        public static readonly Color ButtonRing = new Color(0.55f, 0.55f, 0.55f, 0.85f);
        public static readonly Color ButtonLabel = new Color(0.85f, 0.85f, 0.85f, 1f);

        // Liga/desliga ligado (Correr): disco cinza médio e aro quase branco.
        public static readonly Color ToggledFill = new Color(0.38f, 0.38f, 0.38f, 0.75f);
        public static readonly Color ToggledRing = new Color(0.92f, 0.92f, 0.92f, 0.95f);

        // Piscar do toque: o disco acende em cinza claro e volta ao normal.
        public static readonly Color FlashFill = new Color(0.8f, 0.8f, 0.8f, 0.85f);
        public static readonly Color FlashRing = new Color(1f, 1f, 1f, 1f);

        // Joystick: base cinza, manete cinza escuro; com o dedo na tela os dois clareiam.
        public static readonly Color JoystickBase = new Color(0.5f, 0.5f, 0.5f, 0.55f);
        public static readonly Color JoystickBaseActive = new Color(0.75f, 0.75f, 0.75f, 0.8f);
        public static readonly Color JoystickHandle = new Color(0.22f, 0.22f, 0.22f, 0.85f);
        public static readonly Color JoystickHandleActive = new Color(0.55f, 0.55f, 0.55f, 0.95f);
    }
}
