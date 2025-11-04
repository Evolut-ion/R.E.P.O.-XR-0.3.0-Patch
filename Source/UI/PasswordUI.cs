using RepoXR.Assets;
using RepoXR.ThirdParty.MRTK;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace RepoXR.UI;

public class PasswordUI : MonoBehaviour
{
    private MenuPagePassword passwordPage;
    private NonNativeKeyboard keyboard;
    
    private void Awake()
    {
        passwordPage = GetComponent<MenuPagePassword>();

        // The password menu is shown during the loading phase, so move it over to that canvas
        
        var loadingUi = LoadingUI.instance;
    RepoCompat.AddTrackedDeviceRaycaster(loadingUi.gameObject);
        loadingUi.gameObject.AddComponent<RectMask2D>();
        
        passwordPage.transform.SetParent(loadingUi.transform);
        passwordPage.transform.localPosition = Vector3.zero;
        passwordPage.transform.localRotation = Quaternion.identity;
        passwordPage.transform.localScale = Vector3.one;
        passwordPage.GetComponent<RectTransform>().pivot = Vector2.one * 0.5f;
        
        FindObjectOfType<MenuSelectionBoxTop>().transform.parent.SetParent(loadingUi.transform);
        
        // Create a keyboard

        keyboard = Instantiate(AssetCollection.Keyboard).GetComponent<NonNativeKeyboard>();
        keyboard.transform.position = loadingUi.transform.position + Vector3.down * 1.5f;
        keyboard.transform.rotation = loadingUi.transform.rotation * Quaternion.Euler(15, 0, 0);
        keyboard.PresentKeyboard();
        keyboard.OnKeyboardValueKeyPressed += OnKeyboardKeyPressed;
        keyboard.OnKeyboardFunctionKeyPressed += OnKeyboardFunctionKeyPressed;

        // When joining a lobby we have to create a new interactor manager
        if (!XRRayInteractorManager.Instance)
            new GameObject("Controllers").AddComponent<XRRayInteractorManager>();
        
        XRRayInteractorManager.Instance!.SetLineSortingOrder(15);
    }

    private void OnDestroy()
    {
        keyboard.OnKeyboardValueKeyPressed -= OnKeyboardKeyPressed;
        keyboard.OnKeyboardFunctionKeyPressed -= OnKeyboardFunctionKeyPressed;
    }

    public void OnConfirm()
    {
        keyboard.Close();
        
        XRRayInteractorManager.Instance?.SetLineSortingOrder(0);
    }
    
    private void OnKeyboardKeyPressed(KeyboardValueKey key)
    {
        passwordPage.password += keyboard.IsShifted ? key.ShiftValue : key.Value;
    }

    private void OnKeyboardFunctionKeyPressed(KeyboardKeyFunc key)
    {
        switch (key.ButtonFunction)
        {
            case KeyboardKeyFunc.Function.Enter:
                passwordPage.ConfirmButton();
                return;
            
            case KeyboardKeyFunc.Function.Backspace:
                passwordPage.password = passwordPage.password.Remove(Mathf.Max(passwordPage.password.Length - 1, 0));
                break;
        }
    }
}