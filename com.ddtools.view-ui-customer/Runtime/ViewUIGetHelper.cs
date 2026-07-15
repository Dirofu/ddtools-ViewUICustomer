using UnityEngine;

namespace Core.Scripts.UI.Universal.ViewUICustomer
{
	public class ViewUIGetHelper<T> : MonoBehaviour
	{
		[SerializeField] private T _type;
		public T Type => _type;
	}
}