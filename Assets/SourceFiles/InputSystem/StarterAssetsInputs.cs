using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StarterAssets
{
	public class StarterAssetsInputs : MonoBehaviour
	{
		[Header("Character Input Values")]
		public Vector2 move;
		public Vector2 look;
		public bool jump;
		public bool sprint;

		[Header("Movement Settings")]
		public bool analogMovement;

		[Header("Mouse Cursor Settings")]
		public bool cursorLocked = true;
		public bool cursorInputForLook = true;

		/// <summary>F76: set true by TouchControls while touch is active. A touch drag feeds a pixel
		/// delta into LookInput, like a mouse, so ThirdPersonController.IsCurrentDeviceMouse checks this
		/// too - otherwise the look delta would get multiplied by Time.deltaTime a second time.</summary>
		public bool LookIsPixelDelta;



#if ENABLE_INPUT_SYSTEM
		

		
		public void OnMove(InputValue value)
		{
			MoveInput(value.Get<Vector2>());
		}

		public void OnLook(InputValue value)
		{
			if(cursorInputForLook)
			{
				LookInput(value.Get<Vector2>());
			}
		}

		public void OnJump(InputValue value)
		{
			JumpInput(value.isPressed);
		}

		public void OnSprint(InputValue value)
		{
			SprintInput(value.isPressed);
		}
#endif

			private void Awake()
	{
		SetCursorState(cursorLocked);
		Cursor.visible = false;

		
	}

		public void MoveInput(Vector2 newMoveDirection)
		{
			move = newMoveDirection;
		} 

		public void LookInput(Vector2 newLookDirection)
		{
			look = newLookDirection;
		}

		public void JumpInput(bool newJumpState)
		{
			jump = newJumpState;
		}

		public void SprintInput(bool newSprintState)
		{
			sprint = newSprintState;
		}

		private void OnApplicationFocus(bool hasFocus)
		{
			SetCursorState(cursorLocked);
		}

		private void SetCursorState(bool newState)
		{
			// F76 T5: a browser pointer lock does nothing useful on a tablet and can eat taps, so
			// "locked" means "unlocked" while touch drives the game instead of the mouse.
			bool locked = newState && !TouchInput.Active;
			Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
			Cursor.visible = !locked;
		}
	}
	
}