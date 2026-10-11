import { Icon, type IconName } from "./Icon";

const ICONS: IconName[] = ["user", "chat", "file"];

interface Props {
  label: string;
  steps: readonly { title: string; text: string }[];
  /** Index of the current step; steps before it are shown as done. Leave out for a plain description of the journey. */
  current?: number;
  words: { done: string; current: string };
  compact?: boolean;
}

/**
 * The three-step journey (Sign in → Interview → Your record). On the start page it describes the way; on the interview page it says
 * where the person is, with `aria-current="step"` and the state in words for a screen reader, never by colour alone.
 */
export function Stepper({ label, steps, current, words, compact = false }: Props) {
  return (
    <ol className={compact ? "stepper compact" : "stepper"} aria-label={label}>
      {steps.map((step, i) => {
        const state = current === undefined ? "todo" : i < current ? "done" : i === current ? "current" : "todo";
        return (
          <li key={step.title} className={`step ${state}`} aria-current={state === "current" ? "step" : undefined}>
            <span className="step-badge" aria-hidden="true">
              {state === "done" ? <Icon name="check" size={18} /> : <Icon name={ICONS[i] ?? "spark"} size={18} />}
            </span>
            <span className="step-body">
              <span className="step-title">
                {step.title}
                {state === "done" ? <span className="visually-hidden"> ({words.done})</span> : null}
                {state === "current" ? <span className="visually-hidden"> ({words.current})</span> : null}
              </span>
              {compact ? null : <span className="step-text">{step.text}</span>}
            </span>
          </li>
        );
      })}
    </ol>
  );
}
