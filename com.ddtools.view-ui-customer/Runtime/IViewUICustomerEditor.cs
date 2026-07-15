#if UNITY_EDITOR
using System;

namespace Core.Scripts.UI.Universal.ViewUICustomer
{
	public interface IViewUICustomerEditor
	{
		Type EditorViewType { get; }
		Type EditorElementType { get; }

		bool EditorCaptureState(Enum viewType, ViewUIStateType stateType);
		bool EditorApplyState(Enum viewType, ViewUIStateType stateType);
		bool EditorBeginStateTransition(Enum viewType, ViewUIStateType stateType);
		float EditorTransitionDuration { get; }
		float EditorEvaluateTransition(float normalizedTime);
		void EditorApplyTransition(float progress);
		void EditorCompleteTransition();
		bool EditorHasState(Enum viewType, ViewUIStateType stateType);
		bool EditorValidateCapture(out string error);
		bool EditorRefreshStates();
	}
}
#endif
