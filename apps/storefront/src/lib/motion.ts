export const motionTokens = {
  duration: { instant: 0.1, quick: 0.18, standard: 0.28, deliberate: 0.46, cinematic: 0.8 },
  easing: {
    standard: [0.22, 1, 0.36, 1] as const,
    enter: [0.16, 1, 0.3, 1] as const,
    exit: [0.7, 0, 0.84, 0] as const,
  },
  spring: {
    tactile: { type: "spring" as const, stiffness: 520, damping: 34, mass: 0.7 },
    drawer: { type: "spring" as const, stiffness: 360, damping: 34, mass: 0.9 },
    layout: { type: "spring" as const, stiffness: 330, damping: 32, mass: 0.85 },
  },
} as const;
