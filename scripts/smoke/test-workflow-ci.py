#!/usr/bin/env python3
"""Regression checks for the public fork's GitHub Actions build gate."""

from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS = ROOT / ".github" / "workflows"
def is_immutable_action_ref(ref: str) -> bool:
    """Only repository-local actions or digest/SHA-pinned remote actions are allowed."""
    if ref.startswith("./"):
        return True
    if ref.startswith("docker://"):
        return bool(re.fullmatch(r"docker://[^@\s]+@sha256:[0-9a-f]{64}", ref))
    return bool(re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+@[0-9a-f]{40}", ref))


class WorkflowSafetyTests(unittest.TestCase):
    def test_workflow_only_pull_requests_are_not_ignored(self):
        main = (WORKFLOWS / "main.yml").read_text(encoding="utf-8")
        self.assertRegex(main, r"(?m)^  pull_request:\s*$")
        self.assertNotIn(".github/**", main)

    def test_build_job_token_is_read_only(self):
        main = (WORKFLOWS / "main.yml").read_text(encoding="utf-8")
        self.assertRegex(main, r"(?m)^permissions:\s*\n  contents: read\s*$")

    def test_every_remote_action_is_immutable(self):
        inspected = 0
        for path in sorted(WORKFLOWS.iterdir()):
            if path.suffix not in {".yml", ".yaml"}:
                continue
            for line_no, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
                match = re.match(r"^\\s*(?:-\\s*)?uses:\\s*(\\S+)", line)
                if not match:
                    continue
                ref = match.group(1).strip("'\\\"")
                with self.subTest(workflow=path.name, line=line_no, action=ref):
                    self.assertTrue(is_immutable_action_ref(ref), "Action must use an immutable SHA or digest")
                inspected += 1
        self.assertGreater(inspected, 0, "No action references were inspected")

    def test_mutable_action_tags_are_rejected(self):
        for ref in ("docker/login-action@v3", "azure/setup-helm@v4.3.0",
                    "actions/checkout@main", "docker://alpine:latest"):
            with self.subTest(ref=ref):
                self.assertFalse(is_immutable_action_ref(ref))
        self.assertTrue(is_immutable_action_ref("docker/login-action@" + "a" * 40))
        self.assertTrue(is_immutable_action_ref("docker://alpine@sha256:" + "a" * 64))


if __name__ == "__main__":
    unittest.main()
