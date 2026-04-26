using UnityEngine;
using UnityEngine.Android;

public class EyeTrackingPermission : MonoBehaviour
{
    private void Awake()
    {
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(
                "com.oculus.permission.EYE_TRACKING"))
        {
            Permission.RequestUserPermission(
                "com.oculus.permission.EYE_TRACKING");
            Debug.Log("[EyePerm] Requesting eye tracking permission...");
        }
        else
        {
            Debug.Log("[EyePerm] Eye tracking permission already granted.");
        }
#endif
    }
}