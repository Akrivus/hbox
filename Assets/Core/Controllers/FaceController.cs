using UnityEngine;

public class FaceController : AutoActor
{
    [SerializeField]
    private Transform bodyObject;

    [SerializeField]
    private Transform faceObject;

    [SerializeField, Range(0.1f, 1.0f)]
    private float faceScale = 0.6f;

    private void Update()
    {
        UpdateBodyScale();
        NormalizeFaceScale();
        MoveBodyUp();
    }

    private void UpdateBodyScale()
    {
        var time = Time.time * 0.4f + ActorController.Sentiment.GetHashCode();
        var sin = Mathf.Abs(Mathf.Sin(time) * ActorController.VoiceVolume) * ActorController.Sentiment.Score;
        var hover = Mathf.Sin(time * 0.4f) * (ActorController.Sentiment.Score / 10f);

        bodyObject.transform.localScale = Vector3.one + Vector3.forward * sin;
        bodyObject.transform.localPosition = new Vector3(
            0, 0, bodyObject.transform.localScale.z - 1f + hover);
    }

    private void NormalizeFaceScale()
    {
        var x = 1f / bodyObject.transform.localScale.x;
        var y = 1f / bodyObject.transform.localScale.y;
        var z = 1f / bodyObject.transform.localScale.z;
        faceObject.transform.localScale = new Vector3(x, y, z) * faceScale;
    }

    private void MoveBodyUp()
    {
        var time = Time.time * 0.1f + ActorController.Sentiment.GetHashCode();
        var sin = Mathf.Sin(time) * (ActorController.Sentiment.Score / 50f);
        var position = Vector3.forward * sin;
        position += Vector3.up * 0.2f;

        faceObject.localPosition = Vector3.Lerp(
            faceObject.localPosition,
            position,
            Time.deltaTime * 8.0f);
    }
}
