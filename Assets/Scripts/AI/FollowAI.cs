using UnityEngine;
using System.Collections;

namespace RVP
{
    [RequireComponent(typeof(VehicleParent))]
    [DisallowMultipleComponent]
    [AddComponentMenu("RVP/AI/Follow AI", 0)]

    // Class for following AI
    public class FollowAI : MonoBehaviour
    {
        Rigidbody rigidBody;
        VehicleParent vehicleParent;
        VehicleAssist assist;
        public Transform target;
        Transform targetPrev;
        Rigidbody targetBody;
        Vector3 targetPoint;
        bool targetVisible;
        bool targetIsWaypoint;
        VehicleWaypoint targetWaypoint;

        public float followDistance;
        // bool close;

        [Tooltip("Percentage of maximum speed to drive at")]
        [Range(0, 1)]
        public float speed = 1;
        float initialSpeed;
        float prevSpeed;
        public float targetVelocity = -1;
        float speedLimit = 1;
        float brakeTime;

        [Tooltip("Mask for which objects can block the view of the target")]
        public LayerMask viewBlockMask;
        // Vector3 dirToTarget; // Normalized direction to target
        // float lookDot; // Dot product of forward direction and dirToTarget
        // float steerDot; // Dot product of right direction and dirToTarget

        float stoppedTime;
        float reverseTime;

        [Tooltip("Time limit in seconds which the vehicle is stuck before attempting to reverse")]
        public float stopTimeReverse = 1;

        [Tooltip("Duration in seconds the vehicle will reverse after getting stuck")]
        public float reverseAttemptTime = 1;

        [Tooltip("How many times the vehicle will attempt reversing before resetting, -1 = no reset")]
        public int resetReverseCount = 1;
        int reverseAttempts;

        [Tooltip("Seconds a vehicle will be rolled over before resetting, -1 = no reset")]
        public float rollResetTime = 3;
        float rolledOverTime;

        void Start() {
            rigidBody = GetComponent<Rigidbody>();
            vehicleParent = GetComponent<VehicleParent>();
            assist = GetComponent<VehicleAssist>();
            initialSpeed = speed;

            InitializeTarget();
        }

        void FixedUpdate() 
        {
            if (target) 
            {
                if (target != targetPrev) 
                {
                    InitializeTarget();
                }

                targetPrev = target;

                // Is the target a waypoint?
                targetIsWaypoint = target.GetComponent<VehicleWaypoint>();
                // Can I see the target?
                targetVisible = !Physics.Linecast(transform.position, target.position, viewBlockMask);

                if (targetVisible || targetIsWaypoint) 
                {
                    targetPoint = targetBody ? target.position + targetBody.velocity : target.position;
                }

                if (targetIsWaypoint) 
                {
                    // if vehicle is close enough to target waypoint, switch to the next one
                    // try switching target to a midpoint of the arc of the next n waypoints if that arc distance is within some range
                    if ((transform.position - target.position).sqrMagnitude <= targetWaypoint.radius * targetWaypoint.radius) 
                    {
                        target = targetWaypoint.nextPoint.transform;
                        targetWaypoint = targetWaypoint.nextPoint;
                        prevSpeed = speed;
                        speed = Mathf.Clamp01(targetWaypoint.speed * initialSpeed);
                        brakeTime = prevSpeed / speed;

                        if (brakeTime <= 1) {
                            brakeTime = 0;
                        }
                    }
                }

                brakeTime = Mathf.Max(0, brakeTime - Time.fixedDeltaTime);
                // Is the distance to the target less than the follow distance?
                bool close = (transform.position - target.position).sqrMagnitude <= Mathf.Pow(followDistance, 2) && !targetIsWaypoint;
                Vector3 dirToTarget = (targetPoint - transform.position).normalized;
                float lookDot = Vector3.Dot(vehicleParent.forwardDir, dirToTarget);
                float steerDot = Vector3.Dot(vehicleParent.rightDir, dirToTarget);

                // Attempt to reverse if vehicle is stuck
                stoppedTime = Mathf.Abs(vehicleParent.localVelocity.z) < 1 && !close && vehicleParent.groundedWheels > 0 ? stoppedTime + Time.fixedDeltaTime : 0;

                if (stoppedTime > stopTimeReverse && reverseTime == 0) 
                {
                    reverseTime = reverseAttemptTime;
                    reverseAttempts++;
                }

                // Reset if reversed too many times
                if (reverseAttempts > resetReverseCount && resetReverseCount >= 0) 
                {
                    StartCoroutine(ReverseReset());
                }

                // Set steer input
                if (reverseTime == 0)
                {
                    vehicleParent.SetSteer(Mathf.Abs(Mathf.Pow(steerDot, (transform.position - target.position).sqrMagnitude > 20 ? 1 : 2)) * Mathf.Sign(steerDot));
                }
                else
                {
                    vehicleParent.SetSteer(-Mathf.Sign(steerDot) * (close ? 0 : 1));
                }

                reverseTime = Mathf.Max(0, reverseTime - Time.fixedDeltaTime);

                if (targetVelocity > 0) 
                {
                    speedLimit = Mathf.Clamp01(targetVelocity - vehicleParent.localVelocity.z);
                }
                else {
                    speedLimit = 1;
                }

                // Set accel input
                if (!close && (lookDot > 0 || vehicleParent.localVelocity.z < 5) && vehicleParent.groundedWheels > 0 && reverseTime == 0) 
                {
                    vehicleParent.SetAccel(speed * speedLimit);
                }
                else 
                {
                    vehicleParent.SetAccel(0);
                }

                // Set brake input
                if (reverseTime == 0 && brakeTime == 0 && !(close && vehicleParent.localVelocity.z > 0.1f)) 
                {
                    if (lookDot < 0.5f && lookDot > 0 && vehicleParent.localVelocity.z > 10) 
                    {
                        vehicleParent.SetBrake(0.5f - lookDot);
                    }
                    else 
                    {
                        vehicleParent.SetBrake(0);
                    }
                }
                else {
                    if (reverseTime > 0) 
                    {
                        vehicleParent.SetBrake(1);
                    }
                    else 
                    {
                        if (brakeTime > 0) 
                        {
                            vehicleParent.SetBrake(brakeTime * 0.2f);
                        }
                        else 
                        {
                            vehicleParent.SetBrake(1 - Mathf.Clamp01(Vector3.Distance(transform.position, target.position) / Mathf.Max(0.01f, followDistance)));
                        }
                    }
                }

                // Set ebrake input
                if ((close && vehicleParent.localVelocity.z <= 0.1f) || (lookDot <= 0 && vehicleParent.velMag > 20)) 
                {
                    vehicleParent.SetEbrake(1);
                }
                else 
                {
                    vehicleParent.SetEbrake(0);
                }
            }

            rolledOverTime = assist.rolledOver ? rolledOverTime + Time.fixedDeltaTime : 0;

            // Reset if stuck rolled over
            if (rolledOverTime > rollResetTime && rollResetTime >= 0) 
            {
                StartCoroutine(ResetRotation());
            }
        }

        IEnumerator ReverseReset() 
        {
            reverseAttempts = 0;
            reverseTime = 0;
            yield return new WaitForFixedUpdate();
            transform.position = targetPoint;
            transform.rotation = Quaternion.LookRotation(targetIsWaypoint ? (targetWaypoint.nextPoint.transform.position - targetPoint).normalized : Vector3.forward, GlobalControl.worldUpDir);
            rigidBody.velocity = Vector3.zero;
            rigidBody.angularVelocity = Vector3.zero;
        }

        IEnumerator ResetRotation() 
        {
            yield return new WaitForFixedUpdate();
            transform.eulerAngles = new Vector3(0, transform.eulerAngles.y, 0);
            transform.Translate(Vector3.up, Space.World);
            rigidBody.velocity = Vector3.zero;
            rigidBody.angularVelocity = Vector3.zero;
        }

        public void InitializeTarget() 
        {
            if (target) 
            {
                // if target is a vehicle
                targetBody = target.GetTopmostParentComponent<Rigidbody>();

                // if target is a waypoint
                targetWaypoint = target.GetComponent<VehicleWaypoint>();
                if (targetWaypoint) 
                {
                    prevSpeed = targetWaypoint.speed;
                }
            }
        }
    }
}
