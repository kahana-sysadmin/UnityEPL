using System;
using UnityEngine;

public class ErrorNotification {
    public static IInterfaceManager mainThread = null;
    public ErrorNotification() {}

    public void Notify(Exception e) {
        UnityEngine.Debug.Log(e);

        // TODO: pre processor on is editor
#if UNITY_EDITOR
        throw e;
#else
        if(mainThread == null) {
           throw new ApplicationException("Main thread not registered to error notifier.");
        }
#endif

        mainThread.Do(new EventBase<Exception>(mainThread.Notify, e));
    }
}

public class ErrorPopup : MonoBehaviour {
    public Rect windowRect;

    void OnGUI() {

    }

}
