"use client";

import { ContactShadows, Float, RoundedBox } from "@react-three/drei";
import { Canvas, useFrame } from "@react-three/fiber";
import { useRef } from "react";
import type { Group } from "three";

function ObjectStudy() {
  const group = useRef<Group>(null);
  useFrame(({ clock, pointer }) => {
    if (!group.current) return;
    group.current.rotation.y = clock.getElapsedTime() * 0.16 + pointer.x * 0.16;
    group.current.rotation.x = -0.13 + pointer.y * 0.08;
  });
  return <group ref={group} rotation={[-0.13, -0.5, 0.08]}>
    <Float speed={1.15} rotationIntensity={0.08} floatIntensity={0.22}>
      <RoundedBox args={[2.65, 3.15, 1.12]} radius={0.42} smoothness={5} castShadow>
        <meshStandardMaterial color="#17382f" roughness={0.34} metalness={0.08} />
      </RoundedBox>
      <mesh position={[0, 0.15, 0.59]} castShadow>
        <torusGeometry args={[0.72, 0.1, 24, 80]} />
        <meshStandardMaterial color="#bedb6b" roughness={0.4} />
      </mesh>
      <mesh position={[0, -1.05, 0.61]} castShadow>
        <capsuleGeometry args={[0.12, 0.4, 8, 20]} />
        <meshStandardMaterial color="#ee5d3d" roughness={0.28} />
      </mesh>
    </Float>
  </group>;
}

export function HeroScene({ active }: { active: boolean }) {
  return <Canvas camera={{ position: [0, 0.1, 6.7], fov: 38 }} dpr={[1, 1.5]} frameloop={active ? "always" : "never"} gl={{ antialias: true, powerPreference: "high-performance" }} shadows>
    <color attach="background" args={["#d9d2c3"]} />
    <ambientLight intensity={1.2} />
    <directionalLight position={[4, 6, 5]} intensity={3.3} color="#fff5df" castShadow />
    <pointLight position={[-4, -1, 3]} intensity={18} color="#ee5d3d" distance={8} />
    <ObjectStudy />
    <ContactShadows position={[0, -2.15, 0]} opacity={0.3} scale={5.5} blur={2.8} far={5} />
  </Canvas>;
}
