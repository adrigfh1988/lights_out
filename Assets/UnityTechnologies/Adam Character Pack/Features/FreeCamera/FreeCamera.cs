using UnityEngine;
using UnityEngine.InputSystem;

public class FreeCamera : MonoBehaviour {
	// Matches the old InputManager "Mouse X"/"Mouse Y" axis sensitivity, which
	// scaled the raw mouse delta by this factor.
	const float k_MouseSensitivity = 0.1f;

	// The old "Horizontal"/"Vertical" axes ramped in and out at 3 units/second
	// (sensitivity and gravity were both 3) instead of snapping to -1/0/1.
	const float k_MoveAxisSpeed = 3f;

	// Those axes also had a joystick binding on the left stick, with this deadzone.
	const float k_StickDeadzone = 0.19f;

	public bool enableInputCapture = true;
	public bool holdRightMouseCapture = false;

	public float lookSpeed = 5f;
	public float moveSpeed = 5f;
	public float sprintSpeed = 50f;

	bool	m_inputCaptured;
	float	m_yaw;
	float	m_pitch;
	float	m_horizontal;
	float	m_vertical;

	void Awake() {
		enabled = enableInputCapture;
	}

	void OnValidate() {
		if(Application.isPlaying)
			enabled = enableInputCapture;
	}

	void CaptureInput() {
		Cursor.lockState = CursorLockMode.Locked;

		Cursor.visible = false;
		m_inputCaptured = true;

		m_yaw = transform.eulerAngles.y;
		m_pitch = transform.eulerAngles.x;
	}

	void ReleaseInput() {
		Cursor.lockState = CursorLockMode.None;
		Cursor.visible = true;
		m_inputCaptured = false;
		m_horizontal = 0f;
		m_vertical = 0f;
	}

	void OnApplicationFocus(bool focus) {
		if(m_inputCaptured && !focus)
			ReleaseInput();
	}

	// Smoothed key axis, standing in for the old Input.GetAxis ramp.
	static float MoveAxis(float current, bool negative, bool positive) {
		var target = (positive ? 1f : 0f) - (negative ? 1f : 0f);
		return Mathf.MoveTowards(current, target, k_MoveAxisSpeed * Time.deltaTime);
	}

	// Input.GetAxis merged the key and joystick bindings of an axis; whichever
	// was pushed further won.
	static float WithStick(float keyAxis, float stickAxis) {
		if(Mathf.Abs(stickAxis) < k_StickDeadzone)
			return keyAxis;
		return Mathf.Abs(stickAxis) > Mathf.Abs(keyAxis) ? stickAxis : keyAxis;
	}

	void Update() {
		var mouse = Mouse.current;
		var keyboard = Keyboard.current;

		if(mouse == null || keyboard == null)
			return;

		if(!m_inputCaptured) {
			if(!holdRightMouseCapture && mouse.leftButton.wasPressedThisFrame)
				CaptureInput();
			else if(holdRightMouseCapture && mouse.rightButton.wasPressedThisFrame)
				CaptureInput();
		}

		if(!m_inputCaptured)
			return;

		if(!holdRightMouseCapture && keyboard.escapeKey.wasPressedThisFrame)
			ReleaseInput();
		else if(holdRightMouseCapture && mouse.rightButton.wasReleasedThisFrame)
			ReleaseInput();

		if(!m_inputCaptured)
			return;

		var mouseDelta = mouse.delta.ReadValue() * k_MouseSensitivity;

		m_yaw = (m_yaw + lookSpeed * mouseDelta.x) % 360f;
		m_pitch = (m_pitch - lookSpeed * mouseDelta.y) % 360f;
		transform.rotation = Quaternion.AngleAxis(m_yaw, Vector3.up) * Quaternion.AngleAxis(m_pitch, Vector3.right);

		m_vertical = MoveAxis(m_vertical, keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed,
									      keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed);
		m_horizontal = MoveAxis(m_horizontal, keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed,
											  keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed);

		var stick = Gamepad.current != null ? Gamepad.current.leftStick.ReadValue() : Vector2.zero;

		var speed = Time.deltaTime * (keyboard.leftShiftKey.isPressed ? sprintSpeed : moveSpeed);
		var forward = speed * WithStick(m_vertical, stick.y);
		var right = speed * WithStick(m_horizontal, stick.x);
		var up = speed * ((keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f));
		transform.position += transform.forward * forward + transform.right * right + Vector3.up * up;
	}
}
