import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach } from "vitest";

// Vitest runs without global test hooks, so React Testing Library cannot register its own
// cleanup: without this every render stays in the document and queries find several matches.
afterEach(cleanup);
