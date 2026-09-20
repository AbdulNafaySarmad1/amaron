"use client";

import { motion, type HTMLMotionProps } from "motion/react";
import type { ReactNode } from "react";
import { motionTokens } from "@/lib/motion";

type ButtonProps = Omit<HTMLMotionProps<"button">, "children"> & {
  children?: ReactNode;
  variant?: "primary" | "secondary" | "quiet";
  size?: "small" | "medium" | "large";
  busy?: boolean;
  icon?: ReactNode;
};

export function Button({ variant = "primary", size = "medium", busy = false, disabled, icon, children, className = "", ...props }: ButtonProps) {
  return (
    <motion.button
      className={`button button--${variant} button--${size} ${className}`}
      disabled={disabled || busy}
      aria-busy={busy}
      whileTap={disabled || busy ? undefined : { y: 1 }}
      transition={motionTokens.spring.tactile}
      {...props}
    >
      <span className="button__label">{children}</span>
      {icon ? <span className="button__icon">{icon}</span> : null}
    </motion.button>
  );
}
