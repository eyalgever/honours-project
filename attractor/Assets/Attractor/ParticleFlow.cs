﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class ParticleFlow : MonoBehaviour {

    const int ELECTRIC = 0;
    const int GRAVITY = 1;
    const int SIMPLE = 2;
    const int VORTEX = 3;
    public int simType = ELECTRIC;

    public Gradient particleColourGradient;
    public float forceMultiplier = 1.0f;
    public GameObject targetObject;
    float g = 1f;
    float mass = 2f;

    public OSC osc;
    int numTargets = 0; //targets refer to targets detected by orbbec/opencv

    ParticleSystem ps;
    int currentAttractors = 15;
    List<GameObject> attractors;


    float alpha = 0.1f; //lowpass filter positions for smooth movement, lower number for more smoothing

    public AudioSource audioSource;
    float[] asamples = new float[128];
    float avgFreq = 0.0f;
    float runningAvgFreq = 0.0f;

    // Use this for initialization
    void Start()
    {

        ps = GetComponent<ParticleSystem>();
        initAttractors();
        osc.SetAddressHandler("/numPoints", setNumTargets);
        osc.SetAddressHandler("/points", moveTargets);
    }

    void setNumTargets(OscMessage message)
    {
        numTargets = message.GetInt(0);
        //print("num blobs: " + numTargets);
    }

    void moveTargets(OscMessage message)
    {
        //relay this message to maxMSP
        osc.Send(message);

        //find whether number of targets seen by camera or number of spheres is greater
        if (numTargets > attractors.Count)
        {
            currentAttractors = attractors.Count;
        }
        else
        {
            currentAttractors = numTargets;
            for(int i = numTargets; i < attractors.Count; i++)
            {
                //hide offscreen
                attractors[i].transform.position = new Vector3(-100, -100, 100);
            }
        }

        for (int i=0; i < currentAttractors; i++) {
            float x = message.GetFloat(i*2) / 6f;
            float y = message.GetFloat(i+1) / 3f;
            Vector3 newPos = new Vector3(x , 0, y );
            Vector3 oldPos = attractors[i].transform.position;
            //dampen for smooth movement
            attractors[i].transform.position = newPos * alpha + (oldPos * (1.0f - alpha));
        }
    }

    void Update()
    {
        // add variation to particle colour
        ParticleSystem.MainModule main = GetComponent<ParticleSystem>().main;
        main.startColor = particleColourGradient.Evaluate(Random.Range(0f, 1f));
        GetSpectrumAudioSource();
    }

    void GetSpectrumAudioSource()
    {
        audioSource.GetSpectrumData(asamples, 0, FFTWindow.Blackman);

        avgFreq = 0.0f;
        for(int i = 0; i < asamples.Length; i++)
        {
            avgFreq = avgFreq + asamples[i];
        }
        avgFreq = avgFreq * avgFreq;
        avgFreq = avgFreq / asamples.Length;
        runningAvgFreq = (avgFreq * alpha) + ((1 - alpha) * runningAvgFreq);



    }

    void LateUpdate()
    {

        //put particles of the system into array & update them to gravity algorithm
        ParticleSystem.Particle[] particles = new ParticleSystem.Particle[ps.particleCount];
        ps.GetParticles(particles);

        for (int i = 0; i < particles.Length; i++)
        {
            ParticleSystem.Particle p = particles[i];
            Vector3 particleWorldPosition;
            if (ps.main.simulationSpace == ParticleSystemSimulationSpace.Local)
            {
                particleWorldPosition = transform.TransformPoint(p.position);
            }
            else if (ps.main.simulationSpace == ParticleSystemSimulationSpace.Custom)
            {
                particleWorldPosition = ps.main.customSimulationSpace.TransformPoint(p.position);
            }
            else
            {
                particleWorldPosition = p.position;
            }

            Vector3 totalForce;
            if (simType == SIMPLE)
            {
                totalForce = applySimple(particleWorldPosition);
            } else if (simType == GRAVITY)
            {
                totalForce = applyGravity(particleWorldPosition);
            } else if (simType == ELECTRIC)
            {
                totalForce = applyElectric(p);
            } else if (simType == VORTEX) {
                totalForce = applyVortex(p);
            } else
            {
                totalForce = applySimple(particleWorldPosition);
            }



            if (simType == VORTEX)
            {
                if(currentAttractors == 0)
                {
                    p.velocity = new Vector3(0, 1, 0) * avgFreq * 100;
                } else
                {
                    p.velocity = totalForce * avgFreq * 100;
                }
                
            }
            else if (simType != GRAVITY)
            {
                p.velocity = totalForce;    //velocity only to visualise field line style
                p.velocity = p.velocity * avgFreq * 100;
            }
            else
            {
                p.velocity += totalForce;   //with  acceleration
                float scale = runningAvgFreq * 100+0.75f;
                Color original = particleColourGradient.Evaluate((float)i/particles.Length);
                Color lerpedColor = Color.Lerp(new Color(0, 0, 0), original, scale);
                p.color = lerpedColor;
            }

           

            particles[i] = p;
        }
        ps.SetParticles(particles, particles.Length); //set updated particles into the system
    }

    Vector3 applySimple(Vector3 particleWorldPosition)
    {
        Vector3 direction = Vector3.zero;
  
        for(int i=0; i< currentAttractors; i++)
        {
            GameObject a = attractors[i];
            direction += (a.transform.position - particleWorldPosition).normalized;
        }
        Vector3 totalForce = ((direction) * forceMultiplier) * Time.deltaTime;
        return totalForce;
    }
    /*
     * algo from: https://gamedevelopment.tutsplus.com/tutorials/adding-turbulence-to-a-particle-system--gamedev-13332
     */
    Vector3 applyVortex(ParticleSystem.Particle p)
    {
        float distanceX = float.MaxValue;
        float distanceY = float.MaxValue;
        float distanceZ = float.MaxValue;
        float distance = float.MaxValue;
    
        Vector3 direction = Vector3.zero;
        for (int i = 0; i < currentAttractors; i++)
        {
            GameObject a = attractors[i];
            if(Vector3.Distance(p.position,a.transform.position) < distance)
            {
                distanceX = (p.position.x - a.transform.position.x);
                distanceY = (p.position.y - a.transform.position.y);
                distanceZ = (p.position.z - a.transform.position.z);
                distance = Vector3.Distance(p.position, a.transform.position);
            }

            direction += (a.transform.position - p.position).normalized;
        }

        float vortexScale = 1.0f;
        float vortexSpeed = 1.0f;
        float factor = 1 / (1 + (distanceX * distanceX + distanceZ * distanceZ)/ vortexScale);

        float vx = distanceX  * vortexSpeed;
        float vy = distanceY * vortexSpeed;
        float vz = distanceZ * vortexSpeed;

        Vector3 totalForce = Quaternion.AngleAxis(90, Vector3.up) * new Vector3(vx, 0, vz) * forceMultiplier + (direction);
        return totalForce;
    }
    Vector3 applyGravity(Vector3 particleWorldPosition)
    {
        if (currentAttractors == 0)
        {
            return Vector3.zero;
        }
        Vector3 direction = Vector3.zero;
        Vector3 totalForce = Vector3.zero;
        for (int i = 0; i < currentAttractors; i++)
        {
            GameObject a = attractors[i];
            direction = (a.transform.position - particleWorldPosition).normalized;
            float magnitude = direction.magnitude;
            Mathf.Clamp(magnitude, 5.0f, 10.0f); //eliminate extreme result for very close or very far objects

            float gforce = (g * mass * mass) / direction.magnitude * direction.magnitude;
            totalForce += ((direction) * gforce) * Time.deltaTime;
        }

        totalForce = totalForce * forceMultiplier;
        return totalForce;
    }

    Vector3 applyElectric(ParticleSystem.Particle p)
    {
        Vector3 totalForce = Vector3.zero;
        Vector3 force = Vector3.zero;
        for (int i = 0; i < currentAttractors; i++)
        {
            GameObject a = attractors[i];
            float dist = Vector3.Distance(p.position, a.transform.position) * 100000;
            float fieldMag = 99999 / dist * dist;
            Mathf.Clamp(fieldMag, 0.0f, 5.0f);

            //alternate postive and negative charges
            if (i % 2 == 0)
            {
                force.x -= fieldMag * (p.position.x - a.transform.position.x) / dist;
                force.y -= fieldMag * (p.position.y - a.transform.position.y) / dist;
                force.z -= fieldMag * (p.position.z - a.transform.position.z) / dist;
            }
            else
            {
                force.x += fieldMag * (p.position.x - a.transform.position.x) / dist;
                force.y += fieldMag * (p.position.y - a.transform.position.y) / dist;
                force.z += fieldMag * (p.position.z - a.transform.position.z) / dist;
            }

      
        }
        totalForce = force * forceMultiplier;
        return totalForce;
    }

    void initAttractors()
    {
        attractors = new List<GameObject>();
        for (int i = 0; i < currentAttractors; i++)
        {
            Vector3 pos = new Vector3(i * 3, 0.0f, (currentAttractors - i) * 3);
            Vector3 scale = new Vector3(2.0f, 2.0f, 2.0f);
            GameObject newAttractor = CreateTarget();

            newAttractor.transform.position = pos;
            newAttractor.transform.localScale = scale;
            attractors.Add(newAttractor);
        }
    }
    GameObject CreateTarget()
    {
        GameObject newAttractor;
        if (targetObject == null)
        {
            newAttractor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        }
        else
        {
            newAttractor = Instantiate(targetObject) as GameObject;
        }
        return newAttractor;
    }
}

