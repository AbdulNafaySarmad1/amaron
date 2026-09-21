"use client";
import { createContext, useContext } from "react";

const SessionContext = createContext<{ csrfToken: string }>({ csrfToken: "" });
export const SessionProvider = SessionContext.Provider;
export const useSession = () => useContext(SessionContext);
