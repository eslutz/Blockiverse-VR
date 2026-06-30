using TMPro;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    public interface IBlockiverseSystemKeyboardField
    {
        void Configure(TMP_InputField field);
        void Configure(TMP_InputField field, TouchScreenKeyboardType keyboardType);
    }
}
