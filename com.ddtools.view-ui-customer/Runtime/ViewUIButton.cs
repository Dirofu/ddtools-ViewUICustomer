using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Core.Scripts.UI.Universal.ViewUICustomer
{
	public enum ViewUIButtonClickBehavior
	{
		StayActive = 0,
		ReturnToIdle = 1,
	}

	public class ViewUIButton : MonoBehaviour,
		IPointerClickHandler, 
		IPointerEnterHandler,
		IPointerExitHandler,
		IPointerDownHandler,
		IPointerUpHandler
	{
		
		public virtual event Action ButtonPointerEnter = delegate { };
		public virtual event Action ButtonPointerExit = delegate { };
		public virtual event Action ButtonPointerDown = delegate { };
		public virtual event Action ButtonPointerUp = delegate { };
		public virtual event Action ButtonClicked = delegate { };

		public virtual void OnPointerEnter(PointerEventData eventData)
		{
			ButtonPointerEnter.Invoke();
		}

		public virtual void OnPointerExit(PointerEventData eventData)
		{
			ButtonPointerExit.Invoke();
		}

		public virtual void OnPointerClick(PointerEventData eventData)
		{
			ButtonClicked.Invoke();
		}

		public virtual void OnPointerDown(PointerEventData eventData)
		{
			ButtonPointerDown.Invoke();
		}

		public virtual void OnPointerUp(PointerEventData eventData)
		{
			ButtonPointerUp.Invoke();
		}

	}
}
