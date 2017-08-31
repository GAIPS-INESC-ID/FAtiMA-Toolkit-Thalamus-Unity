using UnityEngine;
using UnityEngine.UI;

public class ButtonTest : MonoBehaviour
{
    public Slider Slider;

    public void DoSomething()
    {
        Debug.Log(Slider.value.ToString());
    }
    public void DoSomethingWithASlider(Slider slider)
    {
        Debug.Log(Slider.value.ToString());
    }
}
