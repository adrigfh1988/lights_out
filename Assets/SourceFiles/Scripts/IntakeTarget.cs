using UnityEngine;

// Its own file (not inside IntakeRoom.cs): Unity can only serialize a component whose class matches its file name.
// Declared in IntakeRoom.cs, the scene embedded a stand-in script and IntakeRoom.bell came back null after any scene load.
/// <summary>F86: the bell the thrown bottle has to hit. A solid collider; the flying bottle carries the Rigidbody, so it is told about the impact.</summary>
public class IntakeTarget : MonoBehaviour
{
    /// <summary>True from the first thrown item that touches it.</summary>
    public bool Hit { get; private set; }

    private void OnCollisionEnter(Collision collision)
    {
        if (Hit) return;
        if (collision.collider.GetComponentInParent<ThrowableItem>() == null) return;
        Strike();
    }

    // The builder gives the bell a generous TRIGGER sphere (a solid one would block the player), so a bottle flying
    // through it counts. Resting bottles and cans have trigger colliders themselves and are ignored.
    private void OnTriggerEnter(Collider other)
    {
        if (Hit || other.isTrigger) return;
        if (other.GetComponentInParent<ThrowableItem>() == null) return;
        Strike();
    }

    /// <summary>Rings the bell. Public so a test (or a future rope) can ring it without a throw.</summary>
    public void Strike()
    {
        if (Hit) return;
        Hit = true;
        AudioSource.PlayClipAtPoint(DoorAudio.BuildClunkClip(0.9f, 1250f, 0.7f), transform.position, 1f);
    }
}
